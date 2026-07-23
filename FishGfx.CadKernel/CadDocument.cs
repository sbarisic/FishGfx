using System.Collections.Concurrent;

namespace FishGfx.Cad;

public sealed class CadKernelException : Exception
{
	public CadKernelException(string operation, string message)
		: base($"{operation}: {message}")
	{
		Operation = operation;
	}

	public string Operation { get; }
}

public readonly record struct CadRevisioned<T>(long Revision, T Value);

public sealed class CadDocument : IAsyncDisposable, IDisposable
{
	private readonly BlockingCollection<Action> queue = new();
	private readonly Thread worker;
	private CadDocumentSafeHandle handle;
	private long revision;
	private bool disposed;

	private CadDocument()
	{
		worker = new Thread(WorkerMain)
		{
			IsBackground = true,
			Name = "FishGfx CAD document worker",
		};
		worker.Start();
	}

	public long Revision => Interlocked.Read(ref revision);

	public static async Task<CadDocument> CreateAsync(CancellationToken cancellationToken = default)
	{
		CadDocument document = new();

		try
		{
			await document.InvokeAsync(() =>
			{
				const uint requiredApiVersion = 4;
				uint apiVersion = NativeMethods.ApiVersion();

				if (apiVersion != requiredApiVersion)
				{
					throw new CadKernelException(
						"Initialize",
						$"Unsupported native CAD ABI version {apiVersion}; expected {requiredApiVersion}."
					);
				}

				Check(NativeMethods.DocumentCreate(out nint nativeHandle), "Create CAD document");
				document.handle = new CadDocumentSafeHandle(nativeHandle);
				return true;
			}, cancellationToken).ConfigureAwait(false);

			return document;
		}
		catch
		{
			document.Dispose();
			throw;
		}
	}

	public Task<long> ImportStepAsync(CadPart part, string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(part);
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		return MutateAsync(() => Check(
			NativeMethods.DocumentImportStep(handle, part.Id.ToString("D"), Path.GetFullPath(path), part.Name),
			"Import STEP"
		), cancellationToken);
	}

	public Task<long> ReplaceStepAsync(CadPart part, string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(part);
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		return MutateAsync(() => Check(
			NativeMethods.DocumentReplaceStep(handle, part.Id.ToString("D"), Path.GetFullPath(path), part.Name),
			"Replace STEP"
		), cancellationToken);
	}

	public Task<long> SetPartTransformAsync(CadPart part, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(part);
		NativeTransform transform = new(part.Transform);

		return MutateAsync(() => Check(
			NativeMethods.DocumentSetPartTransform(handle, part.Id.ToString("D"), in transform),
			"Set part transform"
		), cancellationToken);
	}

	public Task<CadRevisioned<IReadOnlyList<NativeTopologyDescriptor>>> GetTopologyAsync(
		Guid partId,
		CancellationToken cancellationToken = default
	)
	{
		return InvokeAsync(() =>
		{
			string id = partId.ToString("D");
			Check(NativeMethods.DocumentGetTopologyCount(handle, id, out nuint count), "Count topology");
			NativeTopologyInfo[] native = new NativeTopologyInfo[checked((int)count)];

			unsafe
			{
				fixed (NativeTopologyInfo* pointer = native)
				{
					Check(NativeMethods.DocumentCopyTopology(handle, id, pointer, count), "Read topology");
				}
			}

			NativeTopologyDescriptor[] result = native.Select(item => new NativeTopologyDescriptor(
				new CadTopologyRef(partId, item.Id, item.Kind),
				item.Center.ToManaged(),
				item.Axis.ToManaged(),
				item.Radius
			)).ToArray();

			return new CadRevisioned<IReadOnlyList<NativeTopologyDescriptor>>(
				Revision,
				Array.AsReadOnly(result)
			);
		}, cancellationToken);
	}

	public Task<CadRevisioned<MateFrameResult>> GetMateFrameAsync(
		CadTopologyRef topology,
		CadPoint3 localHit,
		CancellationToken cancellationToken = default
	)
	{
		return InvokeAsync(() =>
		{
			NativePoint3 hit = new(localHit);
			Check(NativeMethods.DocumentGetMateFrame(
				handle,
				topology.PartId.ToString("D"),
				topology.TopologyId,
				in hit,
				out NativeFrame frame,
				out double radius
			), "Create mate frame");

			return new CadRevisioned<MateFrameResult>(
				Revision,
				new MateFrameResult(frame.ToManaged(), radius)
			);
		}, cancellationToken);
	}

	public Task<long> BuildRunnerAsync(
		CadRunner runner,
		RunnerEvaluationResult evaluation,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(runner);
		ArgumentNullException.ThrowIfNull(evaluation);

		if (!evaluation.Success || evaluation.RunnerId != runner.Id)
		{
			throw new ArgumentException("Only a valid evaluation of the selected runner can be built.", nameof(evaluation));
		}
		if (evaluation.EditRevision != runner.EditRevision)
		{
			throw new InvalidOperationException("A stale runner evaluation cannot replace current exact geometry.");
		}

		long expectedEditRevision = evaluation.EditRevision;
		Guid runnerId = runner.Id;
		string runnerName = runner.Name;
		NativeRunnerFeature[] features = evaluation.Chain.Features
			.Select(NativeRunnerFeature.FromManaged)
			.ToArray();

		return MutateAsync(() =>
		{
			if (runner.EditRevision != expectedEditRevision)
			{
				throw new InvalidOperationException("A stale runner evaluation cannot replace current exact geometry.");
			}

			unsafe
			{
				fixed (NativeRunnerFeature* pointer = features)
				{
					Check(NativeMethods.DocumentBuildRunner(
						handle,
						runnerId.ToString("D"),
						runnerName,
						pointer,
						(nuint)features.Length
					), "Build exact runner");
				}
			}
		}, cancellationToken);
	}

	internal Task<RunnerEvaluationResult> EvaluateRunnerAsync(
		CadRunner runner,
		IReadOnlyDictionary<Guid, CadMate> mates,
		IReadOnlyDictionary<Guid, CadPart> parts,
		CancellationToken cancellationToken = default
	)
	{
		RunnerGraphPlan plan = RunnerGraphPlanner.Plan(runner, mates, parts);
		if (!plan.Success)
		{
			return Task.FromResult(new RunnerEvaluationResult
			{
				RunnerId = runner.Id,
				OutputNodeId = plan.OutputNodeId,
				EditRevision = plan.EditRevision,
				Diagnostics = plan.Diagnostics,
			});
		}

		return InvokeAsync(() => EvaluatePlan(runner, plan), cancellationToken);
	}

	private static RunnerEvaluationResult EvaluationFailure(
		CadRunner runner,
		RunnerGraphPlan plan,
		CadKernelException exception,
		nuint evaluatedCount
	)
	{
		Guid? nodeId = evaluatedCount < (nuint)plan.Features.Count
			? plan.Features[checked((int)evaluatedCount)].NodeId
			: null;
		return new RunnerEvaluationResult
		{
			RunnerId = runner.Id,
			OutputNodeId = plan.OutputNodeId,
			EditRevision = plan.EditRevision,
			Diagnostics = new[]
			{
				new CadDiagnostic("RUN051", exception.Message, CadDiagnosticSeverity.Error, nodeId),
			},
		};
	}

	private RunnerEvaluationResult EvaluatePlan(CadRunner runner, RunnerGraphPlan plan)
	{
		NativeRunnerFeatureSpec[] specifications = plan.Features
			.Select(NativeRunnerFeatureSpec.FromManaged)
			.ToArray();
		NativeRunnerFeature[] evaluated = new NativeRunnerFeature[specifications.Length];
		NativeFrame startFrame = new(plan.StartFrame);
		NativeRunnerProfile startProfile = NativeRunnerProfile.FromManaged(plan.StartProfile);

		nuint evaluatedCount = 0;
		try
		{
			unsafe
			{
				fixed (NativeRunnerFeatureSpec* specificationPointer = specifications)
				fixed (NativeRunnerFeature* evaluatedPointer = evaluated)
				{
					Check(NativeMethods.EvaluateRunnerFeatures(
						in startFrame,
						in startProfile,
						specificationPointer,
						(nuint)specifications.Length,
						evaluatedPointer,
						(nuint)evaluated.Length,
						out evaluatedCount
					), "Evaluate exact runner");
				}
			}
		}
		catch (CadKernelException exception)
		{
			return EvaluationFailure(runner, plan, exception, evaluatedCount);
		}

		List<RunnerFeature> features = new(evaluated.Length);
		RunnerSectionProfile activeProfile = plan.StartProfile;
		for (int index = 0; index < evaluated.Length; ++index)
		{
			RunnerFeatureSpec specification = plan.Features[index];
			NativeRunnerFeature native = evaluated[index];
			RunnerSectionProfile outputProfile = specification.Kind == RunnerFeatureKind.LoftTransition
				? specification.OutputProfile
				: activeProfile;
			features.Add(new RunnerFeature(
				specification.NodeId,
				specification.Kind,
				native.EntryFrame.ToManaged(),
				native.ExitFrame.ToManaged(),
				activeProfile,
				outputProfile,
				native.Length,
				native.Center.ToManaged(),
				native.Radius,
				native.SweepRadians,
				native.RotationRadians,
				native.Control1.ToManaged(),
				native.Control2.ToManaged()
			));
			activeProfile = outputProfile;
		}

		RunnerFeatureChain chain = new(
			runner.StartMateId,
			features[^1].ExitFrame,
			activeProfile,
			features.AsReadOnly()
		);
		return new RunnerEvaluationResult
		{
			RunnerId = runner.Id,
			OutputNodeId = plan.OutputNodeId,
			EditRevision = plan.EditRevision,
			Chain = chain,
			Diagnostics = Array.Empty<CadDiagnostic>(),
		};
	}

	public Task<long> RemoveRunnerAsync(Guid runnerId, CancellationToken cancellationToken = default)
	{
		return MutateAsync(() => Check(
			NativeMethods.DocumentRemoveRunner(handle, runnerId.ToString("D")),
			"Remove exact runner"
		), cancellationToken);
	}

	public Task<long> RenameRunnerAsync(CadRunner runner, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(runner);
		return MutateAsync(() => Check(
			NativeMethods.DocumentRenameRunner(handle, runner.Id.ToString("D"), runner.Name),
			"Rename exact runner"
		), cancellationToken);
	}

	public Task<long> BindMateSelectorAsync(CadMate mate, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(mate);

		if (!mate.Topology.HasValue)
		{
			throw new ArgumentException("An unresolved mate cannot create an OCAF selector.", nameof(mate));
		}

		CadTopologyRef topology = mate.Topology.Value;
		return MutateAsync(() => Check(
			NativeMethods.DocumentBindTopologySelector(
				handle,
				mate.Id.ToString("D"),
				topology.PartId.ToString("D"),
				topology.TopologyId
			),
			"Bind OCAF topology selector"
		), cancellationToken);
	}

	public Task<CadRevisioned<CadTessellation>> TessellatePartAsync(
		Guid partId,
		double linearDeflection = 0.1,
		double angularDeflection = Math.PI / 18,
		CancellationToken cancellationToken = default
	)
	{
		return InvokeAsync(() =>
		{
			Check(NativeMethods.DocumentTessellatePart(
				handle,
				partId.ToString("D"),
				linearDeflection,
				angularDeflection,
				out nint nativeTessellation
			), "Tessellate part");
			using CadTessellationSafeHandle tessellation = new(nativeTessellation);

			return new CadRevisioned<CadTessellation>(Revision, CopyTessellation(tessellation));
		}, cancellationToken);
	}

	public Task<CadRevisioned<CadTessellation>> TessellateRunnerAsync(
		Guid runnerId,
		double linearDeflection = 0.1,
		double angularDeflection = Math.PI / 18,
		CancellationToken cancellationToken = default
	)
	{
		return InvokeAsync(() =>
		{
			Check(NativeMethods.DocumentTessellateRunner(
				handle,
				runnerId.ToString("D"),
				linearDeflection,
				angularDeflection,
				out nint nativeTessellation
			), "Tessellate runner");
			using CadTessellationSafeHandle tessellation = new(nativeTessellation);

			return new CadRevisioned<CadTessellation>(Revision, CopyTessellation(tessellation));
		}, cancellationToken);
	}

	public Task SaveXcafAsync(string path, CancellationToken cancellationToken = default)
	{
		return InvokeAsync(() =>
		{
			Check(NativeMethods.DocumentSaveXcaf(handle, Path.GetFullPath(path)), "Save XCAF document");
			return true;
		}, cancellationToken);
	}

	public Task<long> LoadXcafAsync(string path, CancellationToken cancellationToken = default)
	{
		return MutateAsync(() => Check(
			NativeMethods.DocumentLoadXcaf(handle, Path.GetFullPath(path)),
			"Load XCAF document"
		), cancellationToken);
	}

	public Task ExportStepAsync(string path, CancellationToken cancellationToken = default)
	{
		return InvokeAsync(() =>
		{
			Check(NativeMethods.DocumentExportStep(handle, Path.GetFullPath(path)), "Export STEP AP242");
			return true;
		}, cancellationToken);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		Task dispose = InvokeAsync(() =>
		{
			handle?.Dispose();
			handle = null;
			return true;
		}, CancellationToken.None);
		dispose.GetAwaiter().GetResult();
		disposed = true;
		queue.CompleteAdding();
		worker.Join();
		queue.Dispose();
	}

	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	private Task<long> MutateAsync(Action action, CancellationToken cancellationToken)
	{
		return InvokeAsync(() =>
		{
			action();
			return Interlocked.Increment(ref revision);
		}, cancellationToken);
	}

	private Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(CadDocument));
		}

		TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationTokenRegistration registration = cancellationToken.Register(() =>
			completion.TrySetCanceled(cancellationToken)
		);

		try
		{
			queue.Add(() =>
			{
				if (completion.Task.IsCompleted)
				{
					registration.Dispose();
					return;
				}

				try
				{
					completion.TrySetResult(action());
				}
				catch (Exception exception)
				{
					completion.TrySetException(exception);
				}
				finally
				{
					registration.Dispose();
				}
			});
		}
		catch
		{
			registration.Dispose();
			throw;
		}

		return completion.Task;
	}

	private void WorkerMain()
	{
		foreach (Action action in queue.GetConsumingEnumerable())
		{
			action();
		}
	}

	private static unsafe CadTessellation CopyTessellation(CadTessellationSafeHandle handle)
	{
		NativeMeshVertex[] vertices = new NativeMeshVertex[checked((int)NativeMethods.TessellationVertexCount(handle))];
		uint[] indices = new uint[checked((int)NativeMethods.TessellationIndexCount(handle))];
		NativeFaceRange[] faces = new NativeFaceRange[checked((int)NativeMethods.TessellationFaceCount(handle))];
		NativeEdgeRange[] edges = new NativeEdgeRange[checked((int)NativeMethods.TessellationEdgeCount(handle))];
		NativePoint3[] edgePoints = new NativePoint3[checked((int)NativeMethods.TessellationEdgePointCount(handle))];

		fixed (NativeMeshVertex* vertexPointer = vertices)
		fixed (uint* indexPointer = indices)
		fixed (NativeFaceRange* facePointer = faces)
		fixed (NativeEdgeRange* edgePointer = edges)
		fixed (NativePoint3* edgePointPointer = edgePoints)
		{
			Check(NativeMethods.TessellationCopy(
				handle,
				vertexPointer,
				(nuint)vertices.Length,
				indexPointer,
				(nuint)indices.Length,
				facePointer,
				(nuint)faces.Length,
				edgePointer,
				(nuint)edges.Length,
				edgePointPointer,
				(nuint)edgePoints.Length,
				out NativePoint3 minimum,
				out NativePoint3 maximum
			), "Copy tessellation");

			return new CadTessellation
			{
				Vertices = vertices.Select(vertex => new CadMeshVertex(
					vertex.X,
					vertex.Y,
					vertex.Z,
					vertex.NormalX,
					vertex.NormalY,
					vertex.NormalZ
				)).ToArray(),
				Indices = indices,
				Faces = faces.Select(face => new CadFaceRange(
					face.TopologyId,
					face.GetSourceNodeId(),
					checked((int)face.FirstIndex),
					checked((int)face.IndexCount)
				)).ToArray(),
				Edges = edges.Select(edge => new CadEdgePolyline(
					edge.TopologyId,
					edge.Kind,
					edgePoints
						.Skip(checked((int)edge.FirstPoint))
						.Take(checked((int)edge.PointCount))
						.Select(point => point.ToManaged())
						.ToArray()
				)).ToArray(),
				Minimum = minimum.ToManaged(),
				Maximum = maximum.ToManaged(),
			};
		}
	}

	private static void Check(NativeStatus status, string operation)
	{
		if (status != NativeStatus.Ok)
		{
			throw new CadKernelException(operation, NativeMethods.LastError());
		}
	}
}

public sealed record NativeTopologyDescriptor(
	CadTopologyRef Topology,
	CadPoint3 Center,
	CadPoint3 Axis,
	double RadiusMillimetres
);

public readonly record struct MateFrameResult(CadFrame Frame, double RadiusMillimetres);
