using System.Numerics;
using FishGfx.Cad;
using FishUI.Controls;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadUi
{
	internal void SetCollector(
		CadCollectorSystem system,
		CadCollectorInlet inlet,
		CadPoint3 position,
		CadPoint3 eulerDegrees
	)
	{
		synchronizing = true;
		collectorEditing = true;
		inspectorTitle.Text = inlet == null
			? $"COLLECTOR: {system.Name}"
			: $"INLET: {inlet.Name}";
		translation[0].Value = (float)position.X;
		translation[1].Value = (float)position.Y;
		translation[2].Value = (float)position.Z;
		rotation[0].Value = (float)eulerDegrees.X;
		rotation[1].Value = (float)eulerDegrees.Y;
		rotation[2].Value = (float)eulerDegrees.Z;
		(string Label, double Value)[] fields = inlet == null
			? new[]
			{
				("Outlet diameter mm", system.OutletProfile.OuterDiameterMillimetres),
				("Outlet wall mm", system.OutletProfile.WallThicknessMillimetres),
				("Outlet stub mm", system.OutletStubLength),
				("Merge length mm", system.MergeLength),
				("Overlap mm", system.OverlapLength),
				("End handle mm", system.BranchEndHandleLength),
			}
			: new[]
			{
				("Merge station", inlet.MergeStation),
				("Branch handle mm", inlet.BranchStartHandleLength),
				("Clocking length mm", inlet.ClockingTransitionLength),
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

	private void OpenCollectorRunnerChecklist()
	{
		if (currentProject == null)
		{
			return;
		}
		HashSet<Guid> bound = currentProject.CollectorSystems.SelectMany(system => system.Inlets)
			.Where(inlet => inlet.Binding != null)
			.Select(inlet => inlet.Binding.RunnerId)
			.ToHashSet();
		CadRunner[] eligible = currentProject.Runners.Where(runner => !bound.Contains(runner.Id))
			.ToArray();
		float dialogHeight = Math.Max(180, 116 + eligible.Length * 28);
		Panel dialog = new()
		{
			ID = "collectorRunnerChecklist",
			Position = Absolute((window.Width - 420) * 0.5f, (window.Height - dialogHeight) * 0.5f),
			Size = new Vector2(420, dialogHeight),
			Variant = PanelVariant.Dark,
		};
		dialog.AddChild(new Label("SELECT COLLECTOR RUNNERS")
		{
			Position = new Vector2(20, 18),
			Size = new Vector2(370, 28),
		});
		List<(CadRunner Runner, CheckBox Check)> choices = new();
		for (int index = 0; index < eligible.Length; ++index)
		{
			CheckBox check = new(eligible[index].Name)
			{
				Position = new Vector2(24, 58 + index * 28),
				Size = new Vector2(360, 24),
				IsChecked = true,
			};
			choices.Add((eligible[index], check));
			dialog.AddChild(check);
		}
		Button create = new()
		{
			Position = new Vector2(204, dialogHeight - 52),
			Size = new Vector2(92, 32),
			Text = "Create",
		};
		create.OnButtonPressed += (_, button, _) =>
		{
			if (button != global::FishUI.FishMouseButton.Left)
			{
				return;
			}
			Guid[] selected = choices.Where(choice => choice.Check.IsChecked)
				.Select(choice => choice.Runner.Id)
				.ToArray();
			ui.RemoveControl(dialog);
			AddCollectorRequested?.Invoke(selected);
		};
		dialog.AddChild(create);
		Button cancel = new()
		{
			Position = new Vector2(306, dialogHeight - 52),
			Size = new Vector2(92, 32),
			Text = "Cancel",
		};
		cancel.OnButtonPressed += (_, button, _) =>
		{
			if (button == global::FishUI.FishMouseButton.Left)
			{
				ui.RemoveControl(dialog);
			}
		};
		dialog.AddChild(cancel);
		ui.AddControl(dialog);
	}
}
