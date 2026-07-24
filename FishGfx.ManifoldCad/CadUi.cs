using System.Numerics;
using FishGfx.Cad;
using FishGfx.FishUI;
using FishGfx.Graphics;
using FishUI.Controls;
using FishUIRuntime = global::FishUI.FishUI;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadUi : IDisposable
{
	private readonly RenderWindow window;
	private readonly FishUIGraphicsBackend graphics;
	private readonly FishUIInputAdapter input;
	private readonly FishUIRuntime ui;
	private readonly Panel toolbar;
	private readonly Panel modelPanel;
	private readonly Panel inspectorPanel;
	private readonly TreeView modelTree;
	private readonly Label inspectorTitle;
	private readonly Label statusLabel;
	private readonly Textbox mateName;
	private readonly Textbox runnerName;
	private readonly NumericUpDown[] translation = new NumericUpDown[3];
	private readonly NumericUpDown[] rotation = new NumericUpDown[3];
	private readonly NumericUpDown[] parameters = new NumericUpDown[6];
	private readonly Label[] parameterLabels = new Label[6];
	private bool collectorEditing;
	private ManifoldProject currentProject;
	private bool synchronizing;
	private bool disposed;

	internal CadUi(RenderWindow window)
	{
		this.window = window ?? throw new ArgumentNullException(nameof(window));
		graphics = new FishUIGraphicsBackend(window);
		input = new FishUIInputAdapter(window);
		input.Enabled = true;
		global::FishUI.FishUISettings settings = new();
		ui = new FishUIRuntime(settings, graphics, input, new NullFishUIEvents(), graphics.FileSystem);
		ui.Init();
		settings.LoadTheme("data/themes/gwen.yaml");

		toolbar = new Panel
		{
			ID = "cadToolbar",
			Position = Absolute(0, 0),
			Size = new Vector2(window.Width, CadLayout.ToolbarHeight),
			Variant = PanelVariant.Dark,
		};
		CreateToolbarButton("Import STEP", 10, () => OpenFile("*.step", FilePickerMode.Open, ImportRequested));
		CreateToolbarButton("Replace", 128, () => OpenFile("*.step", FilePickerMode.Open, ReplaceRequested));
		CreateToolbarButton("Open Project", 226, () => OpenFile("*.fgcad", FilePickerMode.Open, OpenProjectRequested));
		CreateToolbarButton("Save Project", 354, () => OpenFile("*.fgcad", FilePickerMode.Save, SaveProjectRequested));
		CreateToolbarButton("Export STEP", 482, () => OpenFile("*.step", FilePickerMode.Save, ExportRequested));
		CreateToolbarButton("Fit", 600, () => FitRequested?.Invoke());
		CreateToolbarButton("Ortho", 666, () => OrthographicRequested?.Invoke());
		CreateToolbarButton("Top", 742, () => ViewRequested?.Invoke(CadStandardView.Top));
		CreateToolbarButton("Front", 808, () => ViewRequested?.Invoke(CadStandardView.Front));
		CreateToolbarButton("Right", 884, () => ViewRequested?.Invoke(CadStandardView.Right));
		CreateToolbarButton("Gizmo", 962, () => GizmoModeRequested?.Invoke());
		CreateToolbarButton("Pick Ray", 1038, () => PickingRayDebugRequested?.Invoke());
		CreateToolbarButton("Add Node", 1142, () => AddNodeRequested?.Invoke());
		CreateToolbarButton("Row", 1240, () => CollectorPresetRequested?.Invoke(CollectorLayoutPreset.Row));
		CreateToolbarButton("Radial", 1300, () => CollectorPresetRequested?.Invoke(CollectorLayoutPreset.Radial));
		CreateToolbarButton("Staggered", 1380, () => CollectorPresetRequested?.Invoke(CollectorLayoutPreset.Staggered));
		ui.AddControl(toolbar);

		modelPanel = new Panel
		{
			ID = "cadModelPanel",
			Position = Absolute(0, CadLayout.ToolbarHeight),
			Size = new Vector2(CadLayout.LeftWidth, window.Height - CadLayout.ToolbarHeight),
			Variant = PanelVariant.Dark,
		};
		modelPanel.AddChild(new Label("MODEL / MATES")
		{
			Position = new Vector2(16, 14),
			Size = new Vector2(220, 26),
		});
		modelTree = new TreeView
		{
			Position = new Vector2(16, 50),
			Size = new Vector2(224, 480),
			ShowLines = true,
		};
		modelTree.OnNodeSelected += (_, node) =>
		{
			if (node.UserData is TreeIdentity identity)
			{
				switch (identity.Kind)
				{
					case TreeIdentityKind.Part:
						PartSelected?.Invoke(identity.Id);
						break;
					case TreeIdentityKind.Mate:
						MateSelected?.Invoke(identity.Id);
						break;
					case TreeIdentityKind.Runner:
						RunnerSelected?.Invoke(identity.Id);
						break;
					case TreeIdentityKind.Collector:
						CollectorSelected?.Invoke(identity.Id);
						break;
					case TreeIdentityKind.CollectorInlet:
						CollectorInletSelected?.Invoke(identity.Id);
						break;
				}
			}
		};
		modelPanel.AddChild(modelTree);

		Button addRunner = new() { Position = new Vector2(16, 540), Size = new Vector2(108, 32), Text = "Add Runner" };
		addRunner.OnButtonPressed += (_, button, _) =>
		{
			if (button == global::FishUI.FishMouseButton.Left)
				AddRunnerRequested?.Invoke();
		};
		modelPanel.AddChild(addRunner);
		Button deleteRunner = new() { Position = new Vector2(132, 540), Size = new Vector2(108, 32), Text = "Delete Runner" };
		deleteRunner.OnButtonPressed += (_, button, _) =>
		{
			if (button == global::FishUI.FishMouseButton.Left)
				DeleteRunnerRequested?.Invoke();
		};
		modelPanel.AddChild(deleteRunner);
		Button addCollector = new()
		{
			Position = new Vector2(16, 580),
			Size = new Vector2(108, 32),
			Text = "Add Collector",
		};
		addCollector.OnButtonPressed += (_, button, _) =>
		{
			if (button == global::FishUI.FishMouseButton.Left)
				OpenCollectorRunnerChecklist();
		};
		modelPanel.AddChild(addCollector);
		Button deleteCollector = new()
		{
			Position = new Vector2(132, 580),
			Size = new Vector2(108, 32),
			Text = "Delete Collector",
		};
		deleteCollector.OnButtonPressed += (_, button, _) =>
		{
			if (button == global::FishUI.FishMouseButton.Left)
				DeleteCollectorRequested?.Invoke();
		};
		modelPanel.AddChild(deleteCollector);
		runnerName = new Textbox
		{
			Position = new Vector2(16, 660),
			Size = new Vector2(224, 28),
			Placeholder = "Runner name",
		};
		runnerName.OnTextChanged += (_, text) =>
		{
			if (!synchronizing)
			{
				if (currentProject?.View.ActiveCollectorSystemId.HasValue == true
					&& !currentProject.View.ActiveCollectorInletId.HasValue)
				{
					CollectorNameChanged?.Invoke(text);
				}
				else
				{
					RunnerNameChanged?.Invoke(text);
				}
			}
		};
		modelPanel.AddChild(runnerName);
		mateName = new Textbox
		{
			Position = new Vector2(16, 624),
			Size = new Vector2(224, 28),
			Placeholder = "Mate name",
		};
		mateName.OnTextChanged += (_, text) =>
		{
			if (!synchronizing)
			{
				MateNameChanged?.Invoke(text);
			}
		};
		modelPanel.AddChild(mateName);
		Button createMate = new()
		{
			Position = new Vector2(16, 696),
			Size = new Vector2(224, 34),
			Text = "Create / Rebind Mate",
		};
		createMate.OnButtonPressed += (_, button, _) =>
		{
			if (button == global::FishUI.FishMouseButton.Left)
			{
				CreateMateRequested?.Invoke();
			}
		};
		modelPanel.AddChild(createMate);
		Button flipMate = new()
		{
			Position = new Vector2(16, 738),
			Size = new Vector2(224, 34),
			Text = "Flip Mate Axis",
		};
		flipMate.OnButtonPressed += (_, button, _) =>
		{
			if (button == global::FishUI.FishMouseButton.Left)
			{
				FlipMateRequested?.Invoke();
			}
		};
		modelPanel.AddChild(flipMate);
		ui.AddControl(modelPanel);

		inspectorPanel = new Panel
		{
			ID = "cadInspectorPanel",
			Position = Absolute(window.Width - CadLayout.RightWidth, CadLayout.ToolbarHeight),
			Size = new Vector2(CadLayout.RightWidth, window.Height - CadLayout.ToolbarHeight),
			Variant = PanelVariant.Dark,
		};
		inspectorTitle = new Label("INSPECTOR")
		{
			Position = new Vector2(16, 14),
			Size = new Vector2(280, 28),
		};
		inspectorPanel.AddChild(inspectorTitle);
		CreateTransformControls();
		CreateParameterControls();
		statusLabel = new Label("Ready")
		{
			Position = new Vector2(16, 690),
			Size = new Vector2(286, 150),
			Alignment = Align.None,
		};
		inspectorPanel.AddChild(statusLabel);
		ui.AddControl(inspectorPanel);

		Resize(window.Width, window.Height);
		window.Resized += OnResize;
	}

	internal event Action<string> ImportRequested;

	internal event Action<string> ReplaceRequested;

	internal event Action<string> OpenProjectRequested;

	internal event Action<string> SaveProjectRequested;

	internal event Action<string> ExportRequested;

	internal event Action CreateMateRequested;

	internal event Action FlipMateRequested;

	internal event Action<string> MateNameChanged;

	internal event Action<CadPoint3, CadPoint3> TransformChanged;

	internal event Action<int, double> NodeParameterChanged;
	internal event Action<int, double> CollectorParameterChanged;

	internal event Action FitRequested;

	internal event Action OrthographicRequested;

	internal event Action<CadStandardView> ViewRequested;
	internal event Action GizmoModeRequested;
	internal event Action PickingRayDebugRequested;
	internal event Action AddNodeRequested;
	internal event Action AddRunnerRequested;
	internal event Action DeleteRunnerRequested;
	internal event Action<IReadOnlyList<Guid>> AddCollectorRequested;
	internal event Action DeleteCollectorRequested;
	internal event Action<CollectorLayoutPreset> CollectorPresetRequested;
	internal event Action<string> RunnerNameChanged;
	internal event Action<string> CollectorNameChanged;
	internal event Action<Guid> PartSelected;
	internal event Action<Guid> MateSelected;
	internal event Action<Guid> RunnerSelected;
	internal event Action<Guid> CollectorSelected;
	internal event Action<Guid> CollectorInletSelected;

	internal bool InteractionEnabled
	{
		get => input.Enabled;
		set => input.Enabled = value;
	}

	internal void BeginFrame()
	{
		input.BeginFrame();
	}

	internal void Update(float deltaTime, float time)
	{
		ui.TickUpdate(deltaTime, time);
	}

	internal void Render(RenderPass pass, float deltaTime, float time)
	{
		using (graphics.UseRenderPass(pass, pass.View, pass.State))
		{
			ui.TickDraw(deltaTime, time);
		}
	}

	internal void SetModel(
		ManifoldProject project,
		Guid? selectedPartId,
		Guid? selectedMateId,
		Guid? selectedRunnerId
	)
	{
		currentProject = project;
		modelTree.Nodes.Clear();
		TreeNode partsRoot = modelTree.AddNode("PARTS");
		partsRoot.IsExpanded = true;
		foreach (CadPart part in project.Parts)
		{
			TreeNode partNode = partsRoot.AddChild(part.Name, new TreeIdentity(TreeIdentityKind.Part, part.Id));
			partNode.IsExpanded = true;
			partNode.IsSelected = part.Id == selectedPartId;
			foreach (CadMate mate in project.Mates.Where(mate => mate.PartId == part.Id))
			{
				string state = mate.IsResolved ? "linked" : "UNRESOLVED";
				TreeNode mateNode = partNode.AddChild($"{mate.Name} [{state}]",
					new TreeIdentity(TreeIdentityKind.Mate, mate.Id));
				mateNode.IsSelected = mate.Id == selectedMateId;
			}
		}
		TreeNode runnersRoot = modelTree.AddNode("RUNNERS");
		runnersRoot.IsExpanded = true;
		foreach (CadRunner runner in project.Runners)
		{
			CadMate mate = project.Mates.FirstOrDefault(item => item.Id == runner.StartMateId);
			TreeNode node = runnersRoot.AddChild($"{runner.Name} -> {mate?.Name ?? "missing mate"}",
				new TreeIdentity(TreeIdentityKind.Runner, runner.Id));
			node.IsSelected = runner.Id == selectedRunnerId;
		}
		TreeNode collectorsRoot = modelTree.AddNode("COLLECTORS");
		collectorsRoot.IsExpanded = true;
		foreach (CadCollectorSystem system in project.CollectorSystems)
		{
			TreeNode systemNode = collectorsRoot.AddChild(
				$"{system.Name} [{(system.IsResolved ? "current" : "STALE")}]",
				new TreeIdentity(TreeIdentityKind.Collector, system.Id)
			);
			systemNode.IsExpanded = true;
			systemNode.IsSelected = system.Id == project.View.ActiveCollectorSystemId;
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				CadRunner member = project.Runners.FirstOrDefault(
					runner => runner.Id == inlet.Binding?.RunnerId);
				TreeNode inletNode = systemNode.AddChild(
					$"{inlet.Name} -> {member?.Name ?? "missing runner"}",
					new TreeIdentity(TreeIdentityKind.CollectorInlet, inlet.Id)
				);
				inletNode.IsSelected = inlet.Id == project.View.ActiveCollectorInletId;
			}
		}

		synchronizing = true;
		mateName.Text = project.Mates.FirstOrDefault(mate => mate.Id == selectedMateId)?.Name ?? string.Empty;
		CadCollectorSystem selectedCollector = project.View.ActiveCollectorSystemId.HasValue
			&& !project.View.ActiveCollectorInletId.HasValue
			? project.CollectorSystems.FirstOrDefault(system =>
				system.Id == project.View.ActiveCollectorSystemId.Value)
			: null;
		runnerName.Placeholder = selectedCollector == null ? "Runner name" : "Collector name";
		runnerName.Text = selectedCollector?.Name
			?? project.Runners.FirstOrDefault(runner => runner.Id == selectedRunnerId)?.Name
			?? string.Empty;
		synchronizing = false;
	}

	internal void SetPart(CadPart part, CadPoint3 eulerDegrees)
	{
		synchronizing = true;
		collectorEditing = false;
		inspectorTitle.Text = part == null ? "INSPECTOR" : "PART: " + part.Name;
		CadPoint3 position = part?.Transform.Translation ?? default;
		translation[0].Value = (float)position.X;
		translation[1].Value = (float)position.Y;
		translation[2].Value = (float)position.Z;
		rotation[0].Value = (float)eulerDegrees.X;
		rotation[1].Value = (float)eulerDegrees.Y;
		rotation[2].Value = (float)eulerDegrees.Z;
		synchronizing = false;
	}

	internal void SetNode(RunnerNode node)
	{
		synchronizing = true;
		collectorEditing = false;

		for (int index = 0; index < parameters.Length; index++)
		{
			parameters[index].Visible = false;
			parameterLabels[index].Visible = false;
		}

		if (node != null)
		{
			inspectorTitle.Text = RunnerNodes.TryGet(node.DefinitionId, out RunnerNodeDefinition definition)
				? "NODE: " + definition.Title
				: "NODE: Missing definition";
			(string Name, string Label)[] fields = node.DefinitionId switch
			{
				RunnerNodes.StartRunner => new[] { ("wallThickness", "Mate wall mm") },
				RunnerNodes.Straight => new[] { ("length", "Length mm") },
				RunnerNodes.Bend => new[]
				{
					("radius", "Radius mm"),
					("angle", "Angle deg"),
					("rotation", "Rotation deg"),
				},
				RunnerNodes.CubicBezier => new[]
				{
					("startHandleLength", "P1 tangent mm"),
					("control2T", "P2 local T mm"),
					("endT", "P3 local T mm"),
				},
				RunnerNodes.CircularPipe => new[]
				{
					("outerDiameter", "Outer diameter mm"),
					("wallThickness", "Wall thickness mm"),
				},
				RunnerNodes.LoftTransition => new[]
				{
					("length", "Loft length mm"),
					("rotation", "Profile rotation deg"),
				},
				RunnerNodes.ClockingTransition => new[]
				{
					("length", "Clocking length mm"),
					("rotation", "Profile roll deg"),
				},
				_ => Array.Empty<(string, string)>(),
			};

			for (int index = 0; index < fields.Length; index++)
			{
				parameterLabels[index].Text = fields[index].Label;
				parameterLabels[index].Visible = true;
				parameters[index].Visible = true;
				parameters[index].Value = node.Properties.TryGetValue(fields[index].Name, out string text)
					&& float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
					? value
					: 0;
			}
		}

		synchronizing = false;
	}

	internal void SetBezierDraft(BezierDraftState draft, RunnerPathPointKind pointKind)
	{
		ArgumentNullException.ThrowIfNull(draft);
		synchronizing = true;
		(string Label, double Value)[] fields = pointKind switch
		{
			RunnerPathPointKind.Control1 => new[]
			{
				("P1 tangent mm", draft.StartHandleLength),
			},
			RunnerPathPointKind.Control2 => new[]
			{
				("P2 local T mm", draft.Control2Local.X),
				("P2 local U mm", draft.Control2Local.Y),
				("P2 local V mm", draft.Control2Local.Z),
			},
			RunnerPathPointKind.End => new[]
			{
				("P3 local T mm", draft.EndLocal.X),
				("P3 local U mm", draft.EndLocal.Y),
				("P3 local V mm", draft.EndLocal.Z),
			},
			_ => Array.Empty<(string, double)>(),
		};
		for (int index = 0; index < parameters.Length; ++index)
		{
			bool visible = index < fields.Length;
			parameterLabels[index].Visible = visible;
			parameters[index].Visible = visible;
			if (visible)
			{
				parameterLabels[index].Text = fields[index].Label;
				parameters[index].Value = (float)fields[index].Value;
			}
		}
		synchronizing = false;
	}

	internal void SetStatus(string text, bool error = false)
	{
		statusLabel.Text = text;
		statusLabel.SetColorOverride(
			"Text",
			error
				? new global::FishUI.FishColor(150, 35, 35)
				: new global::FishUI.FishColor(30, 105, 60)
		);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		window.Resized -= OnResize;
		input.Dispose();
		graphics.Dispose();
	}

	private void CreateToolbarButton(string text, float x, Action action)
	{
		Button button = new()
		{
			Position = new Vector2(x, 8),
			Size = new Vector2(text.Length * 8 + 24, 32),
			Text = text,
		};
		button.OnButtonPressed += (_, mouseButton, _) =>
		{
			if (mouseButton == global::FishUI.FishMouseButton.Left)
			{
				action();
			}
		};
		toolbar.AddChild(button);
	}

	private void CreateTransformControls()
	{
		inspectorPanel.AddChild(new Label("Placement (mm / deg)")
		{
			Position = new Vector2(16, 58),
			Size = new Vector2(280, 24),
		});
		string[] labels = { "X", "Y", "Z", "RX", "RY", "RZ" };

		for (int index = 0; index < 6; index++)
		{
			int row = index % 3;
			float y = 92 + row * 38 + (index >= 3 ? 128 : 0);
			inspectorPanel.AddChild(new Label(labels[index])
			{
				Position = new Vector2(16, y + 4),
				Size = new Vector2(34, 24),
			});
			NumericUpDown field = new(0, -100000, 100000, index < 3 ? 1 : 5)
			{
				Position = new Vector2(54, y),
				Size = new Vector2(238, 28),
				DecimalPlaces = 2,
			};
			field.OnValueChanged += (_, _) => RaiseTransformChanged();
			inspectorPanel.AddChild(field);

			if (index < 3)
			{
				translation[index] = field;
			}
			else
			{
				rotation[index - 3] = field;
			}
		}
	}

	private void CreateParameterControls()
	{
		for (int index = 0; index < parameters.Length; index++)
		{
			int captured = index;
			float y = 354 + index * 55;
			parameterLabels[index] = new Label
			{
				Position = new Vector2(16, y),
				Size = new Vector2(280, 22),
				Visible = false,
			};
			parameters[index] = new NumericUpDown(0, -100000, 100000, 1)
			{
				Position = new Vector2(16, y + 25),
				Size = new Vector2(276, 28),
				DecimalPlaces = 2,
				Visible = false,
			};
			parameters[index].OnValueChanged += (_, value) =>
			{
				if (!synchronizing)
				{
					if (collectorEditing)
					{
						CollectorParameterChanged?.Invoke(captured, value);
					}
					else
					{
						NodeParameterChanged?.Invoke(captured, value);
					}
				}
			};
			inspectorPanel.AddChild(parameterLabels[index]);
			inspectorPanel.AddChild(parameters[index]);
		}
	}

	private void RaiseTransformChanged()
	{
		if (synchronizing)
		{
			return;
		}

		TransformChanged?.Invoke(
			new CadPoint3(translation[0].Value, translation[1].Value, translation[2].Value),
			new CadPoint3(rotation[0].Value, rotation[1].Value, rotation[2].Value)
		);
	}

	private void OpenFile(string filter, FilePickerMode mode, Action<string> callback)
	{
		FilePickerDialog dialog = new(mode, graphics.FileSystem, Environment.CurrentDirectory, filter)
		{
			Position = Absolute((window.Width - 500) * 0.5f, (window.Height - 400) * 0.5f),
		};
		dialog.OnFileConfirmed += (_, path) => callback?.Invoke(path);
		ui.AddControl(dialog);
	}

	private void OnResize(object sender, WindowResizeEventArgs args)
	{
		Resize(args.Width, args.Height);
	}

	private void Resize(int width, int height)
	{
		ui.Resized(width, height);
		toolbar.Size = new Vector2(width, CadLayout.ToolbarHeight);
		modelPanel.Size = new Vector2(CadLayout.LeftWidth, height - CadLayout.ToolbarHeight);
		inspectorPanel.Position = Absolute(width - CadLayout.RightWidth, CadLayout.ToolbarHeight);
		inspectorPanel.Size = new Vector2(CadLayout.RightWidth, height - CadLayout.ToolbarHeight);
	}

	private static global::FishUI.FishUIPosition Absolute(float x, float y)
	{
		return new global::FishUI.FishUIPosition(global::FishUI.PositionMode.Absolute, new Vector2(x, y));
	}
}

internal enum CadStandardView
{
	Top,
	Bottom,
	Front,
	Back,
	Left,
	Right,
}

internal enum TreeIdentityKind
{
	Part,
	Mate,
	Runner,
	Collector,
	CollectorInlet,
}

internal readonly record struct TreeIdentity(TreeIdentityKind Kind, Guid Id);
