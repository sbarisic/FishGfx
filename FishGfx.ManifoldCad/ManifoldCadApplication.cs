using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using FishGfx.Cad;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;

namespace FishGfx.ManifoldCad;

internal sealed class ManifoldCadApplication : IDisposable
{
	private const int InitialWidth = 1600;
	private const int InitialHeight = 1000;
	private readonly bool autoMode;
	private readonly Stopwatch timer = Stopwatch.StartNew();
	private readonly Dictionary<Guid, CadPoint3> eulerByPart = new();
	private RenderWindow window;
	private InputManager input;
	private CadUi ui;
	private CadViewport viewport;
	private CadNodeCanvas nodeCanvas;
	private GraphicsFont font;
	private Camera uiCamera;
	private CadDocument document;
	private ManifoldProject project = new();
	private RunnerEvaluationResult evaluation;
	private CadPart selectedPart;
	private CadMate selectedMate;
	private NativeTopologyDescriptor selectedTopology;
	private bool hasSelectedTopology;
	private float scrollDelta;
	private double previousTime;
	private bool disposed;
	private bool autoFrameCaptured;
	private int autoVisibleSamples;
	private string autoScreenshotPath;

	internal ManifoldCadApplication(string[] args)
	{
		autoMode = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
	}

	internal void Run()
	{
		window = new RenderWindow(InitialWidth, InitialHeight, "FishGfx Parametric Manifold CAD", true);
		input = new InputManager(window);
		ui = new CadUi(window);
		viewport = new CadViewport(window.Graphics);
		nodeCanvas = new CadNodeCanvas();
		font = new TrueTypeFont(Path.Combine(AppContext.BaseDirectory, "data", "fonts", "Consolas-Regular.ttf"));
		uiCamera = new Camera();
		ConfigureUiCamera(window.Width, window.Height);
		document = CadDocument.CreateAsync().GetAwaiter().GetResult();
		window.Scrolled += OnScroll;
		window.Resized += OnResize;
		WireEvents();
		if (autoMode)
		{
			ConfigureAutomaticFixture();
		}

		evaluation ??= project.EvaluateRunner();
		RefreshUi();
		previousTime = timer.Elapsed.TotalSeconds;

		while (!window.IsCloseRequested)
		{
			input.BeginFrame();
			ui.BeginFrame();
			scrollDelta = 0;
			window.PollEvents();
			double now = timer.Elapsed.TotalSeconds;
			float deltaTime = (float)Math.Max(now - previousTime, 0);
			previousTime = now;
			Update(deltaTime, (float)now);
			Render(deltaTime, (float)now);

			if (autoMode && now >= 2.5)
			{
				ValidateAutomaticFrame();
				window.IsCloseRequested = true;
			}
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		if (window != null)
		{
			window.Scrolled -= OnScroll;
			window.Resized -= OnResize;
		}

		document?.Dispose();
		viewport?.Dispose();
		font?.Dispose();
		ui?.Dispose();
		input?.Dispose();
		window?.Graphics.CollectGarbage();
		window?.Dispose();
	}

	private void WireEvents()
	{
		ui.ImportRequested += ImportStep;
		ui.ReplaceRequested += ReplaceStep;
		ui.OpenProjectRequested += OpenProject;
		ui.SaveProjectRequested += SaveProject;
		ui.ExportRequested += ExportStep;
		ui.CreateMateRequested += CreateOrRebindMate;
		ui.FlipMateRequested += FlipMate;
		ui.MateNameChanged += RenameMate;
		ui.TransformChanged += TransformPart;
		ui.NodeParameterChanged += ChangeNodeParameter;
		ui.FitRequested += viewport.Fit;
		ui.OrthographicRequested += viewport.ToggleOrthographic;
		ui.ViewRequested += viewport.SetView;
		ui.GizmoModeRequested += () =>
		{
			bool rotating = viewport.ToggleGizmoMode();
			ui.SetStatus(rotating ? "Rotation gizmo active." : "Translation gizmo active.");
		};
		viewport.SelectionChanged += SelectViewportItem;
		viewport.GizmoTranslationChanged += translation =>
		{
			CadPoint3 euler = selectedPart != null && eulerByPart.TryGetValue(selectedPart.Id, out CadPoint3 value)
				? value
				: default;
			TransformPart(translation, euler);
		};
		viewport.GizmoRotationChanged += euler =>
		{
			CadPoint3 translation = selectedPart?.Transform.Translation ?? default;
			TransformPart(translation, euler);
		};
		nodeCanvas.SelectionChanged += node =>
		{
			ui.SetNode(node);
		};
	}

	private void Update(float deltaTime, float time)
	{
		if (input.WasKeyPressed(Key.Escape))
		{
			window.IsCloseRequested = true;
		}

		CadRect viewportBounds = CadLayout.Viewport(window.Width, window.Height);
		CadRect graphBounds = CadLayout.Graph(window.Width);
		Vector2 mouse = input.MousePosition;
		viewport.OnScroll(viewportBounds.Contains(mouse) ? scrollDelta : 0);
		viewport.Update(viewportBounds, input, mouse);
		nodeCanvas.Update(project.Graph, graphBounds, input, mouse, graphBounds.Contains(mouse) ? scrollDelta : 0);
		ui.Update(deltaTime, time);
	}

	private void Render(float deltaTime, float time)
	{
		CadRect viewportBounds = CadLayout.Viewport(window.Width, window.Height);
		CadRect graphBounds = CadLayout.Graph(window.Width);
		RenderState overlayState = RenderState.Default with
		{
			CullMode = CullMode.None,
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
		};

		using RenderFrame frame = window.Graphics.BeginFrame();
		RenderTarget viewportTarget = viewport.Render(frame, viewportBounds, nodeCanvas.SelectedNode?.Id);

		using (RenderPass pass = frame.BeginPass(window.Graphics.Backbuffer, new RenderPassDescriptor
		{
			View = new RenderView(uiCamera),
			State = overlayState,
			ColorLoadAction = RenderLoadAction.Clear,
			DepthLoadAction = RenderLoadAction.Clear,
			StencilLoadAction = RenderLoadAction.Clear,
			ClearColor = new Color(15, 18, 22),
		}))
		{
			pass.DrawTexturedRectangle(
				viewportBounds.X,
				viewportBounds.Y,
				viewportBounds.Width,
				viewportBounds.Height,
				0,
				1,
				1,
				0,
				Color.White,
				viewportTarget.ColorAttachments[0]
			);
			nodeCanvas.Render(pass, project.Graph, graphBounds, font, evaluation);
			ui.Render(pass, deltaTime, time);
		}

		if (autoMode && time >= 2.5f && !autoFrameCaptured)
		{
			window.ReadPixels();
			autoVisibleSamples = CountVisibleSamples();
			autoScreenshotPath = Path.GetFullPath(Path.Combine("artifacts", "manifold-cad-auto.png"));
			Directory.CreateDirectory(Path.GetDirectoryName(autoScreenshotPath));
			CaptureScreenshot(autoScreenshotPath);
			autoFrameCaptured = true;
		}

		frame.Present();
	}

	private void ImportStep(string path)
	{
		TryOperation(() =>
		{
			CadPart part = project.AddPart(Path.GetFileNameWithoutExtension(path), Path.GetFullPath(path));
			document.ImportStepAsync(part, path).GetAwaiter().GetResult();
			selectedPart = part;
			eulerByPart[part.Id] = default;
			UploadPart(part);
			viewport.Fit();
			RefreshUi();
			ui.SetStatus($"Imported {part.Name}");
		});
	}

	private void ReplaceStep(string path)
	{
		if (selectedPart == null)
		{
			ui.SetStatus("Select a part before replacing it.", true);
			return;
		}

		TryOperation(() =>
		{
			project.ReplacePart(selectedPart.Id, Path.GetFullPath(path));
			document.ReplaceStepAsync(selectedPart, path).GetAwaiter().GetResult();
			UploadPart(selectedPart);
			RegenerateRunner();
			RefreshUi();
			ui.SetStatus("Part replaced; attached mates require explicit rebinding.", true);
		});
	}

	private void TransformPart(CadPoint3 translation, CadPoint3 euler)
	{
		if (selectedPart == null)
		{
			return;
		}

		TryOperation(() =>
		{
			selectedPart.Transform = new CadTransform(translation, CadQuaternion.FromEulerDegrees(euler));
			eulerByPart[selectedPart.Id] = euler;
			document.SetPartTransformAsync(selectedPart).GetAwaiter().GetResult();
			UploadPart(selectedPart);
			RegenerateRunner();
			ui.SetStatus("Placement updated; attached runner regenerated.");
		});
	}

	private void CreateOrRebindMate()
	{
		if (!hasSelectedTopology || selectedPart == null)
		{
			ui.SetStatus("Select a cyan mate candidate or supported topology first.", true);
			return;
		}

		if (selectedTopology.Topology.Kind is not CadTopologyKind.CircularEdge
			and not CadTopologyKind.CylindricalFace
			and not CadTopologyKind.ClosedProfile)
		{
			ui.SetStatus("Mates require a circular edge, cylindrical face, or detected closed profile.", true);
			return;
		}

		TryOperation(() =>
		{
			CadRevisioned<MateFrameResult> result = document.GetMateFrameAsync(
				selectedTopology.Topology,
				viewport.Selection.HitPoint
			).GetAwaiter().GetResult();
			RunnerNode mateReference = project.Graph.Nodes.FirstOrDefault(node =>
				node.DefinitionId == RunnerNodes.MateReference);
			CadMate referencedMate = mateReference != null
				&& mateReference.Properties.TryGetValue("mateId", out string mateIdText)
				&& Guid.TryParse(mateIdText, out Guid mateId)
				? project.Mates.FirstOrDefault(mate => mate.Id == mateId && mate.PartId == selectedPart.Id)
				: null;
			selectedMate ??= referencedMate;
			selectedMate ??= project.Mates.FirstOrDefault(mate => mate.PartId == selectedPart.Id && !mate.IsResolved);
			selectedMate ??= project.AddMate(selectedPart.Id, $"Mate {project.Mates.Count + 1}");
			selectedMate.Rebind(selectedTopology.Topology, result.Value.Frame, result.Value.RadiusMillimetres);
			document.BindMateSelectorAsync(selectedMate).GetAwaiter().GetResult();

			if (project.Graph.Nodes.Count == 0)
			{
				project.Graph = RunnerGraph.CreateDefault(selectedMate.Id);
			}
			else if (mateReference != null)
			{
				mateReference.Properties["mateId"] = selectedMate.Id.ToString("D");
			}

			RegenerateRunner();
			RefreshUi();
			ui.SetStatus($"Mate '{selectedMate.Name}' bound to exact topology.");
		});
	}

	private void ChangeNodeParameter(int index, double value)
	{
		RunnerNode node = nodeCanvas.SelectedNode;

		if (node == null)
		{
			return;
		}

		string[] properties = nodeCanvas.EditableProperties();

		if (index >= properties.Length)
		{
			return;
		}

		node.Properties[properties[index]] = value.ToString("G17", CultureInfo.InvariantCulture);
		RegenerateRunner();
	}

	private void FlipMate()
	{
		if (selectedMate?.IsResolved != true)
		{
			ui.SetStatus("Select a resolved mate before flipping its axis.", true);
			return;
		}

		selectedMate.Flip();
		RegenerateRunner();
		RefreshUi();
	}

	private void RenameMate(string name)
	{
		if (selectedMate == null || string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		selectedMate.Name = name.Trim();
		RefreshUi();
	}

	private void RegenerateRunner()
	{
		evaluation = project.EvaluateRunner();

		if (!evaluation.Success)
		{
			viewport.MarkRunnerStale();
			ui.SetStatus(string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)), true);
			return;
		}

		long revision = document.BuildRunnerAsync(evaluation).GetAwaiter().GetResult();
		CadRevisioned<CadTessellation> preview = document.TessellateRunnerAsync().GetAwaiter().GetResult();

		if (preview.Revision == revision && revision == document.Revision)
		{
			viewport.AddOrReplace(null, preview.Value, true);
			ui.SetStatus($"Runner {evaluation.LengthMillimetres:F2} mm | exact solid valid");
		}
	}

	private void UploadPart(CadPart part)
	{
		CadRevisioned<CadTessellation> preview = document.TessellatePartAsync(part.Id).GetAwaiter().GetResult();

		if (preview.Revision == document.Revision)
		{
			viewport.AddOrReplace(part.Id, preview.Value, false);
		}

		CadRevisioned<IReadOnlyList<NativeTopologyDescriptor>> topology = document.GetTopologyAsync(part.Id)
			.GetAwaiter()
			.GetResult();

		if (topology.Revision == document.Revision)
		{
			viewport.SetMateCandidates(part, topology.Value);
			viewport.SetMates(project);
		}
	}

	private void SelectViewportItem(CadViewportSelection selection)
	{
		if (selection.MateId.HasValue)
		{
			selectedMate = project.Mates.FirstOrDefault(mate => mate.Id == selection.MateId.Value);
			nodeCanvas.SelectBySource(
				project.Graph.Nodes.FirstOrDefault(node => node.DefinitionId == RunnerNodes.MateReference)?.Id,
				project.Graph
			);
		}

		if (selection.SourceNodeId.HasValue)
		{
			nodeCanvas.SelectBySource(selection.SourceNodeId, project.Graph);
		}

		if (!selection.PartId.HasValue)
		{
			return;
		}

		selectedPart = project.Parts.FirstOrDefault(part => part.Id == selection.PartId.Value);
		selectedMate = project.Mates.FirstOrDefault(mate => mate.PartId == selection.PartId.Value
			&& mate.Topology?.TopologyId == selection.TopologyId);
		CadRevisioned<IReadOnlyList<NativeTopologyDescriptor>> topology = document.GetTopologyAsync(
			selection.PartId.Value
		).GetAwaiter().GetResult();
		selectedTopology = topology.Value.FirstOrDefault(item => item.Topology.TopologyId == selection.TopologyId);
		hasSelectedTopology = selectedTopology != null;
		RefreshUi();

		if (hasSelectedTopology && selection.IsMateCandidate)
		{
			CreateOrRebindMate();
			return;
		}

		if (hasSelectedTopology)
		{
			bool mateEligible = selectedTopology.Topology.Kind is CadTopologyKind.CircularEdge
				or CadTopologyKind.CylindricalFace
				or CadTopologyKind.ClosedProfile;
			ui.SetStatus(
				mateEligible
					? $"Selected {selectedTopology.Topology.Kind}; click Create / Rebind Mate."
					: $"Selected {selectedTopology.Topology.Kind}; choose a cyan candidate sphere or supported topology."
			);
		}
	}

	private void SaveProject(string path)
	{
		TryOperation(() =>
		{
			string temporary = Path.Combine(Path.GetTempPath(), $"fishgfx-{Guid.NewGuid():N}.xbf");

			try
			{
				document.SaveXcafAsync(temporary).GetAwaiter().GetResult();
				CadProjectArchive.Save(path, project, File.ReadAllBytes(temporary));
			}
			finally
			{
				File.Delete(temporary);
			}

			ui.SetStatus($"Saved {Path.GetFileName(path)} atomically.");
		});
	}

	private void OpenProject(string path)
	{
		TryOperation(() =>
		{
			CadProjectPackage package = CadProjectArchive.Load(path);
			string temporary = Path.Combine(Path.GetTempPath(), $"fishgfx-{Guid.NewGuid():N}.xbf");

			try
			{
				File.WriteAllBytes(temporary, package.ModelDocument);
				document.LoadXcafAsync(temporary).GetAwaiter().GetResult();
			}
			finally
			{
				File.Delete(temporary);
			}

			project = package.Project;
			selectedPart = project.Parts.FirstOrDefault();
			selectedMate = project.Mates.FirstOrDefault();

			foreach (CadPart part in project.Parts)
			{
				UploadPart(part);
			}

			RegenerateRunner();
			viewport.Fit();
			RefreshUi();
			ui.SetStatus($"Opened {Path.GetFileName(path)} without source STEP dependencies.");
		});
	}

	private void ExportStep(string path)
	{
		if (evaluation?.Success != true)
		{
			ui.SetStatus("Export is disabled until the exact runner regenerates successfully.", true);
			return;
		}

		TryOperation(() =>
		{
			document.ExportStepAsync(path).GetAwaiter().GetResult();
			ui.SetStatus($"Exported complete AP242 assembly to {Path.GetFileName(path)}.");
		});
	}

	private void RefreshUi()
	{
		viewport.SetMates(project);
		CadPoint3 euler = selectedPart != null && eulerByPart.TryGetValue(selectedPart.Id, out CadPoint3 value)
			? value
			: default;
		viewport.SetSelectedPart(selectedPart, euler);
		ui.SetModel(project, selectedPart?.Id, selectedMate?.Id);
		ui.SetPart(selectedPart, euler);
		ui.SetNode(nodeCanvas.SelectedNode);
	}

	private void TryOperation(Action action)
	{
		try
		{
			action();
		}
		catch (Exception exception)
		{
			ui.SetStatus(exception.Message, true);
		}
	}

	private void ValidateAutomaticFrame()
	{
		if (!ui.InteractionEnabled)
		{
			throw new InvalidOperationException("FishUI input must remain enabled in the CAD workspace.");
		}

		if (!autoFrameCaptured || autoVisibleSamples < 4)
		{
			throw new InvalidOperationException("Automatic graphical validation captured an empty frame.");
		}

		if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FISHGFX_MANIFOLD_AUTO_STEP"))
			&& viewport.MateCandidateCount == 0)
		{
			throw new InvalidOperationException("Automatic STEP validation found no visible mate candidates.");
		}

		Console.WriteLine(
			$"MANIFOLD_CAD_AUTO_OK renderer={window.Graphics.Capabilities.Renderer} "
			+ $"fishUiInput=enabled vertices=exact-runner visibleSamples={autoVisibleSamples} "
			+ $"mateCandidates={viewport.MateCandidateCount} "
			+ $"screenshot={autoScreenshotPath}"
		);
	}

	private int CountVisibleSamples()
	{
		int count = 0;

		for (int y = 20; y < window.Height; y += 80)
			for (int x = 20; x < window.Width; x += 80)
			{
				Color color = window.GetPixel(x, y);

				if (color.A != 0 && (color.R != 0 || color.G != 0 || color.B != 0))
				{
					count++;
				}
			}

		return count;
	}

	private void ConfigureAutomaticFixture()
	{
		CadPart part = project.AddPart("Automated flange fixture");
		string stepPath = Environment.GetEnvironmentVariable("FISHGFX_MANIFOLD_AUTO_STEP");
		part.Transform = string.IsNullOrWhiteSpace(stepPath)
			? new CadTransform(new CadPoint3(15, -8, 4), CadQuaternion.Identity)
			: CadTransform.Identity;
		CadMate mate = project.AddMate(part.Id, "Cylinder 1");

		if (!string.IsNullOrWhiteSpace(stepPath))
		{
			document.ImportStepAsync(part, stepPath).GetAwaiter().GetResult();
			NativeTopologyDescriptor candidate = document.GetTopologyAsync(part.Id).GetAwaiter().GetResult().Value
				.Where(item => item.Topology.Kind == CadTopologyKind.ClosedProfile && item.Axis.Z > 0.5)
				.OrderByDescending(item => item.RadiusMillimetres)
				.First();
			MateFrameResult frame = document.GetMateFrameAsync(candidate.Topology, candidate.Center)
				.GetAwaiter()
				.GetResult()
				.Value;
			mate.Rebind(candidate.Topology, frame.Frame, frame.RadiusMillimetres);
			document.BindMateSelectorAsync(mate).GetAwaiter().GetResult();
			UploadPart(part);
		}
		else
		{
			mate.Rebind(
				new CadTopologyRef(part.Id, 1, CadTopologyKind.CircularEdge),
				new CadFrame(CadPoint3.Zero, new CadPoint3(1, 0, 0), new CadPoint3(0, 1, 0)),
				21.2
			);
		}

		project.Graph = RunnerGraph.CreateDefault(mate.Id);
		selectedPart = part;
		selectedMate = mate;
		evaluation = project.EvaluateRunner();
		document.BuildRunnerAsync(evaluation).GetAwaiter().GetResult();
		CadRevisioned<CadTessellation> preview = document.TessellateRunnerAsync().GetAwaiter().GetResult();
		viewport.AddOrReplace(null, preview.Value, true);
		viewport.SetView(string.IsNullOrWhiteSpace(stepPath) ? CadStandardView.Right : CadStandardView.Front);
		viewport.Fit();
		RunnerNode bend = project.Graph.Nodes.Single(node => node.DefinitionId == RunnerNodes.Bend);
		nodeCanvas.SelectBySource(bend.Id, project.Graph);
	}

	private unsafe void CaptureScreenshot(string path)
	{
		using System.Drawing.Bitmap bitmap = new(window.Width, window.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		System.Drawing.Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
		System.Drawing.Imaging.BitmapData data = bitmap.LockBits(
			rectangle,
			System.Drawing.Imaging.ImageLockMode.WriteOnly,
			System.Drawing.Imaging.PixelFormat.Format32bppArgb
		);

		try
		{
			for (int y = 0; y < bitmap.Height; y++)
			{
				byte* row = (byte*)data.Scan0 + y * data.Stride;

				for (int x = 0; x < bitmap.Width; x++)
				{
					Color color = window.GetPixel(x, y);
					row[x * 4] = color.B;
					row[x * 4 + 1] = color.G;
					row[x * 4 + 2] = color.R;
					row[x * 4 + 3] = color.A;
				}
			}
		}
		finally
		{
			bitmap.UnlockBits(data);
		}

		bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
	}

	private void OnScroll(object sender, ScrollEventArgs args) => scrollDelta += args.Offset.Y;

	private void OnResize(object sender, WindowResizeEventArgs args) => ConfigureUiCamera(args.Width, args.Height);

	private void ConfigureUiCamera(int width, int height) => uiCamera.SetOrthogonal(0, 0, width, height);
}
