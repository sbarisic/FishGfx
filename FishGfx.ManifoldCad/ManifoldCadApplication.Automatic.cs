using FishGfx.Cad;
using FishGfx.Graphics;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
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

		if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FISHGFX_MANIFOLD_AUTO_STEP"))
			&& !viewport.CanPickMateCandidate(CadLayout.Viewport(window.Width, window.Height)))
		{
			throw new InvalidOperationException("Automatic rotated-view validation could not pick a mate candidate.");
		}

		Console.WriteLine(
			$"MANIFOLD_CAD_AUTO_OK renderer={window.Graphics.Capabilities.Renderer} "
			+ $"fishUiInput=enabled vertices=exact-runner visibleSamples={autoVisibleSamples} "
			+ $"frames={autoRenderedFrames} "
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
		NativeTopologyDescriptor[] automaticCandidates = null;
		part.Transform = string.IsNullOrWhiteSpace(stepPath)
			? new CadTransform(new CadPoint3(15, -8, 4), CadQuaternion.Identity)
			: CadTransform.Identity;
		CadMate mate = project.AddMate(part.Id, "Cylinder 1");

		if (!string.IsNullOrWhiteSpace(stepPath))
		{
			document.ImportStepAsync(part, stepPath).GetAwaiter().GetResult();
			automaticCandidates = document.GetTopologyAsync(part.Id).GetAwaiter().GetResult().Value
				.Where(item => item.Topology.Kind == CadTopologyKind.ClosedProfile && item.Axis.Z > 0.5)
				.OrderByDescending(item => item.RadiusMillimetres)
				.ToArray();
			NativeTopologyDescriptor candidate = automaticCandidates.First();
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

		CadRunner runner = project.AddRunner(mate.Id);
		selectedPart = part;
		selectedMate = mate;
		evaluation = project.EvaluateRunner(runner);
		evaluations[runner.Id] = evaluation;
		document.BuildRunnerAsync(runner, evaluation).GetAwaiter().GetResult();
		CadRevisioned<CadTessellation> preview = document.TessellateRunnerAsync(runner.Id).GetAwaiter().GetResult();
		viewport.AddOrReplace(null, runner.Id, preview.Value, true);
		viewport.SetActiveRunner(runner.Id);

		if (automaticCandidates?.Length > 1)
		{
			NativeTopologyDescriptor secondCandidate = automaticCandidates[1];
			CadMate secondMate = project.AddMate(part.Id, "Cylinder 2");
			MateFrameResult secondFrame = document.GetMateFrameAsync(secondCandidate.Topology, secondCandidate.Center)
				.GetAwaiter().GetResult().Value;
			secondMate.Rebind(secondCandidate.Topology, secondFrame.Frame, secondFrame.RadiusMillimetres);
			document.BindMateSelectorAsync(secondMate).GetAwaiter().GetResult();
			CadRunner secondRunner = project.AddRunner(secondMate.Id);
			RunnerEvaluationResult secondEvaluation = project.EvaluateRunner(secondRunner);
			evaluations[secondRunner.Id] = secondEvaluation;
			document.BuildRunnerAsync(secondRunner, secondEvaluation).GetAwaiter().GetResult();
			CadTessellation secondPreview = document.TessellateRunnerAsync(secondRunner.Id).GetAwaiter().GetResult().Value;
			viewport.AddOrReplace(null, secondRunner.Id, secondPreview, true);
			project.SetActiveRunner(runner.Id);
			viewport.SetActiveRunner(runner.Id);
		}

		if (string.IsNullOrWhiteSpace(stepPath))
		{
			viewport.SetView(CadStandardView.Right);
		}
		else
		{
			viewport.SetOrbit(38, 24, false);
		}

		viewport.Fit();

		if (!string.IsNullOrWhiteSpace(stepPath)
			&& !viewport.TryCapturePickingRayToVisibleCandidate(CadLayout.Viewport(window.Width, window.Height)))
		{
			throw new InvalidOperationException("Automatic fixture could not capture its debug picking ray.");
		}

		RunnerNode bend = runner.Graph.Nodes.Single(node => node.DefinitionId == RunnerNodes.Bend);
		nodeCanvas.SelectBySource(bend.Id, runner.Graph);
	}

	private unsafe void CaptureScreenshot(string path)
	{
		using System.Drawing.Bitmap bitmap = new(window.Width, window.Height,
			System.Drawing.Imaging.PixelFormat.Format32bppArgb);
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
}
