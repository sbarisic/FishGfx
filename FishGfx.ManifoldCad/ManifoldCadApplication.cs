using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using FishGfx.Cad;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication : IDisposable
{
	private const int InitialWidth = 1600;
	private const int InitialHeight = 1000;
	private const double InteractiveLinearDeflection = 0.5;
	private const double InteractiveAngularDeflection = Math.PI / 12;
	private readonly bool autoMode;
	private readonly Stopwatch timer = Stopwatch.StartNew();
	private readonly Dictionary<Guid, CadPoint3> eulerByPart = new();
	private readonly Dictionary<Guid, RunnerEvaluationResult> evaluations = new();
	private readonly Dictionary<Guid, string> runnerBuildErrors = new();
	private readonly RunnerGraph emptyGraph = new();
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
	private CadRunner ActiveRunner => project.ActiveRunner;
	private RunnerGraph ActiveGraph => ActiveRunner?.Graph ?? emptyGraph;
	private CadPart selectedPart;
	private CadMate selectedMate;
	private NativeTopologyDescriptor selectedTopology;
	private bool hasSelectedTopology;
	private float scrollDelta;
	private double previousTime;
	private bool disposed;
	private bool autoFrameCaptured;
	private int autoRenderedFrames;
	private int autoVisibleSamples;
	private string autoScreenshotPath;
	private string[] bezierInspectorProperties = Array.Empty<string>();
	private CollectorDraftState collectorDraft;

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

		evaluation = ActiveRunner != null
			? project.EvaluateRunnerAsync(document, ActiveRunner).GetAwaiter().GetResult()
			: null;
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
		ui.CollectorNameChanged += RenameCollector;
		ui.TransformChanged += TransformSelection;
		ui.NodeParameterChanged += ChangeNodeParameter;
		ui.CollectorParameterChanged += ChangeCollectorParameter;
		ui.AddNodeRequested += () =>
		{
			if (ActiveRunner == null)
			{
				ui.SetStatus("Select or add a runner before adding graph nodes.");
				return;
			}

			nodeCanvas.OpenPalette(ActiveGraph, CadLayout.Graph(window.Width));
		};
		ui.AddRunnerRequested += AddRunner;
		ui.DeleteRunnerRequested += DeleteActiveRunner;
		ui.AddCollectorRequested += AddCollector;
		ui.DeleteCollectorRequested += DeleteActiveCollector;
		ui.CollectorPresetRequested += ApplyCollectorPreset;
		ui.RunnerNameChanged += RenameRunner;
		ui.PartSelected += SelectPart;
		ui.MateSelected += SelectMate;
		ui.RunnerSelected += SelectRunner;
		ui.CollectorSelected += SelectCollector;
		ui.CollectorInletSelected += SelectCollectorInlet;
		ui.FitRequested += viewport.Fit;
		ui.OrthographicRequested += viewport.ToggleOrthographic;
		ui.ViewRequested += viewport.SetView;
		ui.GizmoModeRequested += () =>
		{
			bool rotating = viewport.ToggleGizmoMode();
			ui.SetStatus(rotating ? "Rotation gizmo active." : "Translation gizmo active.");
		};
		ui.PickingRayDebugRequested += () =>
		{
			bool enabled = viewport.TogglePickingRayDebug();
			ui.SetStatus(enabled
				? "Pick-ray debug enabled; click in the model viewport."
				: "Pick-ray debug disabled.");
		};
		viewport.SelectionChanged += SelectViewportItem;
		viewport.GizmoTranslationChanged += PreviewGizmoTranslation;
		viewport.GizmoRotationChanged += PreviewGizmoRotation;
		viewport.GizmoCommitRequested += CommitCollectorDraft;
		nodeCanvas.SelectionChanged += node =>
		{
			ui.SetNode(node);
			UpdateBezierEditor(node);
		};
		viewport.BezierCommitRequested += CommitBezierDraft;
		viewport.BezierDraftCancelled += RestoreCommittedBezierDraft;
		viewport.BezierDraftPreviewChanged += ShowBezierDraftInInspector;
		nodeCanvas.GraphChanged += () =>
		{
			if (ActiveRunner != null)
			{
				CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(candidate =>
					candidate.Inlets.Any(inlet => inlet.Binding?.RunnerId == ActiveRunner.Id));
				if (system == null)
				{
					ActiveRunner.CommitEdit();
				}
				else
				{
					system.CommitEdit();
				}
				RegenerateRunner(ActiveRunner);
			}
		};
		nodeCanvas.StatusChanged += ui.SetStatus;
	}

	private void Update(float deltaTime, float time)
	{
		if (input.WasKeyPressed(Key.Escape))
		{
			if (!CancelCollectorDraft() && !viewport.CancelBezierDraft())
			{
				window.IsCloseRequested = true;
			}
		}

		CadRect viewportBounds = CadLayout.Viewport(window.Width, window.Height);
		CadRect graphBounds = CadLayout.Graph(window.Width);
		Vector2 mouse = input.MousePosition;
		viewport.OnScroll(viewportBounds.Contains(mouse) ? scrollDelta : 0);
		viewport.Update(viewportBounds, input, mouse);
		nodeCanvas.Update(ActiveGraph, graphBounds, input, mouse, graphBounds.Contains(mouse) ? scrollDelta : 0);
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
			nodeCanvas.Render(pass, ActiveGraph, graphBounds, font, evaluation);
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

		if (autoMode)
		{
			autoRenderedFrames++;
		}
	}

	private void RefreshUi()
	{
		viewport.SetMates(project);
		viewport.SetCollectors(project);
		CadPoint3 euler = selectedPart != null && eulerByPart.TryGetValue(selectedPart.Id, out CadPoint3 value)
			? value
			: default;
		if (project.ActiveCollectorSystem != null)
		{
			CadCollectorInlet activeInlet = project.ActiveCollectorSystem.Inlets.FirstOrDefault(
				inlet => inlet.Id == project.View.ActiveCollectorInletId);
			CadFrame frame = collectorDraft?.SystemId == project.ActiveCollectorSystem.Id
				&& collectorDraft.InletId == activeInlet?.Id
				? collectorDraft.Frame
				: activeInlet == null
					? project.ActiveCollectorSystem.OutletFrame
					: project.ActiveCollectorSystem.GetWorldInletFrame(activeInlet);
			viewport.SetSelectedFrame(frame, collectorDraft?.EulerDegrees ?? default);
		}
		else
		{
			viewport.SetSelectedPart(selectedPart, euler);
		}
		ui.SetModel(project, selectedPart?.Id, selectedMate?.Id, ActiveRunner?.Id);
		if (project.ActiveCollectorSystem != null)
		{
			CadCollectorInlet inlet = project.ActiveCollectorSystem.Inlets.FirstOrDefault(
				item => item.Id == project.View.ActiveCollectorInletId);
			CadFrame frame = inlet?.LocalFrame ?? project.ActiveCollectorSystem.OutletFrame;
			ui.SetCollector(
				project.ActiveCollectorSystem,
				inlet,
				frame.Origin,
				frame.ToEulerDegrees()
			);
		}
		else
		{
			ui.SetPart(selectedPart, euler);
		}
		if (project.ActiveCollectorSystem == null)
		{
			ui.SetNode(nodeCanvas.SelectedNode);
		}
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

	private void OnScroll(object sender, ScrollEventArgs args) => scrollDelta += args.Offset.Y;

	private void OnResize(object sender, WindowResizeEventArgs args) => ConfigureUiCamera(args.Width, args.Height);

	private void ConfigureUiCamera(int width, int height) => uiCamera.SetOrthogonal(0, 0, width, height);
}
