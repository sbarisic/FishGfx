using System.Numerics;
using FishGfx.Cad;
using FishGfx.FishUI;
using FishGfx.Graphics;
using FishUI.Controls;
using FishUIRuntime = global::FishUI.FishUI;

namespace FishGfx.ManifoldCad;

internal sealed class CadUi : IDisposable
{
	private readonly RenderWindow window;
	private readonly FishUIGraphicsBackend graphics;
	private readonly FishUIInputAdapter input;
	private readonly FishUIRuntime ui;
	private readonly Panel toolbar;
	private readonly Panel modelPanel;
	private readonly Panel inspectorPanel;
	private readonly Label modelLabel;
	private readonly Label inspectorTitle;
	private readonly Label statusLabel;
	private readonly Textbox mateName;
	private readonly NumericUpDown[] translation = new NumericUpDown[3];
	private readonly NumericUpDown[] rotation = new NumericUpDown[3];
	private readonly NumericUpDown[] parameters = new NumericUpDown[3];
	private readonly Label[] parameterLabels = new Label[3];
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
		modelLabel = new Label("No STEP parts imported")
		{
			Position = new Vector2(16, 50),
			Size = new Vector2(226, 600),
			Alignment = Align.None,
		};
		modelPanel.AddChild(modelLabel);
		mateName = new Textbox
		{
			Position = new Vector2(16, 638),
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
			Position = new Vector2(16, 676),
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
			Position = new Vector2(16, 718),
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
			Position = new Vector2(16, 620),
			Size = new Vector2(286, 180),
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

	internal event Action FitRequested;

	internal event Action OrthographicRequested;

	internal event Action<CadStandardView> ViewRequested;
	internal event Action GizmoModeRequested;

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

	internal void SetModel(ManifoldProject project, Guid? selectedPartId, Guid? selectedMateId)
	{
		List<string> lines = new();

		foreach (CadPart part in project.Parts)
		{
			lines.Add($"{(part.Id == selectedPartId ? ">" : " ")} {part.Name}");

			foreach (CadMate mate in project.Mates.Where(mate => mate.PartId == part.Id))
			{
				string state = mate.IsResolved ? "linked" : "UNRESOLVED";
				lines.Add($"  {(mate.Id == selectedMateId ? ">" : "-")} {mate.Name} [{state}]");
			}
		}

		modelLabel.Text = lines.Count == 0 ? "No STEP parts imported" : string.Join("\n", lines);
		synchronizing = true;
		mateName.Text = project.Mates.FirstOrDefault(mate => mate.Id == selectedMateId)?.Name ?? string.Empty;
		synchronizing = false;
	}

	internal void SetPart(CadPart part, CadPoint3 eulerDegrees)
	{
		synchronizing = true;
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
				RunnerNodes.Straight => new[] { ("length", "Length mm") },
				RunnerNodes.Bend => new[]
				{
					("radius", "Radius mm"),
					("angle", "Angle deg"),
					("rotation", "Rotation deg"),
				},
				RunnerNodes.CircularPipe => new[]
				{
					("outerDiameter", "Outer diameter mm"),
					("wallThickness", "Wall thickness mm"),
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
			float y = 354 + index * 62;
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
					NodeParameterChanged?.Invoke(captured, value);
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
