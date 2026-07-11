using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics
{
	/// <summary>
	/// Stores typed graphics commands for explicit, reusable replay.
	/// </summary>
	/// <remarks>
	/// Recording is context-free. Execution must occur on the active graphics-context thread.
	/// Referenced graphics resources remain owned by the caller.
	/// </remarks>
	public sealed class CommandList
	{
		private readonly List<GraphicsCommand> commands = new List<GraphicsCommand>();
		private readonly ReadOnlyCollection<GraphicsCommand> readOnlyCommands;

		public CommandList()
		{
			readOnlyCommands = commands.AsReadOnly();
		}

		public IReadOnlyList<GraphicsCommand> Commands => readOnlyCommands;
		public int Count => commands.Count;
		public bool IsExecuting { get; private set; }
		public GraphicsCommand this[int index] => commands[index];

		public GraphicsCommandBatch Snapshot()
		{
			return new GraphicsCommandBatch(commands);
		}

		public T Add<T>(T command)
			where T : GraphicsCommand
		{
			EnsureMutable();

			if (command == null)
				throw new ArgumentNullException(nameof(command));

			commands.Add(command);
			return command;
		}

		public bool Remove(GraphicsCommand command)
		{
			EnsureMutable();

			if (command == null)
				throw new ArgumentNullException(nameof(command));

			return commands.Remove(command);
		}

		public void RemoveAt(int index)
		{
			EnsureMutable();
			commands.RemoveAt(index);
		}

		public void Clear()
		{
			EnsureMutable();
			commands.Clear();
		}

		public void Execute()
		{
			if (IsExecuting)
				throw new InvalidOperationException("A command list cannot execute recursively.");

			IsExecuting = true;

			try
			{
				foreach (GraphicsCommand command in commands)
					command.Execute();
			}
			finally
			{
				IsExecuting = false;
			}
		}

		public ClearCommand RecordClear(Color color, bool clearColor = true, bool clearDepth = true, bool clearStencil = true)
		{
			return Add(new ClearCommand(color, clearColor, clearDepth, clearStencil));
		}

		public ClearCommand RecordClear()
		{
			return RecordClear(new Color(69, 112, 56));
		}

		public ClearDepthCommand RecordClearDepth(float value = 1)
		{
			return Add(new ClearDepthCommand(value));
		}

		public ClearStencilCommand RecordClearStencil(int value = 0)
		{
			return Add(new ClearStencilCommand(value));
		}

		public PushRenderStateCommand RecordPushRenderState(RenderState state)
		{
			return Add(new PushRenderStateCommand(state));
		}

		public PopRenderStateCommand RecordPopRenderState()
		{
			return Add(new PopRenderStateCommand());
		}

		public Point2DCommand RecordPoint(Vertex2[] points)
		{
			return Add(new Point2DCommand(points));
		}

		public Point2DCommand RecordPoint(Vertex2 point)
		{
			return RecordPoint(new[] { point });
		}

		public Point2DCommand RecordPoint(Vertex2[] points, float thickness)
		{
			return Add(new Point2DCommand(points, thickness));
		}

		public Point2DCommand RecordPoint(Vertex2 point, float thickness)
		{
			return RecordPoint(new[] { point }, thickness);
		}

		public Point3DCommand RecordPoint(Vertex3[] points)
		{
			return Add(new Point3DCommand(points));
		}

		public Point3DCommand RecordPoint(Vertex3 point)
		{
			return RecordPoint(new[] { point });
		}

		public Point3DCommand RecordPoint(Vertex3[] points, float thickness)
		{
			return Add(new Point3DCommand(points, thickness));
		}

		public Point3DCommand RecordPoint(Vertex3 point, float thickness)
		{
			return RecordPoint(new[] { point }, thickness);
		}

		public Line2DCommand RecordLine(Vertex2 start, Vertex2 end, float thickness = 1)
		{
			return Add(new Line2DCommand(start, end, thickness));
		}

		public Line3DCommand RecordLine(Vertex3 start, Vertex3 end, float thickness = 1)
		{
			return Add(new Line3DCommand(start, end, thickness));
		}

		public LineStrip2DCommand RecordLineStrip(Vertex2[] points, float thickness = 1)
		{
			return Add(new LineStrip2DCommand(points, thickness));
		}

		public RectangleCommand RecordRectangle(
			float x,
			float y,
			float width,
			float height,
			float thickness = 1,
			Color? color = null
		)
		{
			return Add(new RectangleCommand(x, y, width, height, thickness, color));
		}

		public RectangleCommand RecordRectangle(
			Vector2 position,
			Vector2 size,
			float thickness = 1,
			Color? color = null
		)
		{
			return RecordRectangle(position.X, position.Y, size.X, size.Y, thickness, color);
		}

		public FilledRectangleCommand RecordFilledRectangle(
			float x,
			float y,
			float width,
			float height,
			Color? color = null
		)
		{
			return Add(new FilledRectangleCommand(x, y, width, height, color));
		}

		public FilledRectangleCommand RecordFilledRectangle(Vector2 position, Vector2 size, Color? color = null)
		{
			return RecordFilledRectangle(position.X, position.Y, size.X, size.Y, color);
		}

		public TexturedRectangleCommand RecordTexturedRectangle(
			float x,
			float y,
			float width,
			float height,
			float u0 = 0,
			float v0 = 0,
			float u1 = 1,
			float v1 = 1,
			Color? color = null,
			Texture texture = null,
			ShaderProgram shader = null
		)
		{
			return Add(new TexturedRectangleCommand(x, y, width, height, u0, v0, u1, v1, color, texture, shader));
		}

		public TexturedRectangleCommand RecordTexturedRectangle(
			Vector2 position,
			Vector2 size,
			Vector2 uvMin,
			Vector2 uvMax,
			Color? color = null,
			Texture texture = null,
			ShaderProgram shader = null
		)
		{
			return RecordTexturedRectangle(
				position.X,
				position.Y,
				size.X,
				size.Y,
				uvMin.X,
				uvMin.Y,
				uvMax.X,
				uvMax.Y,
				color,
				texture,
				shader
			);
		}

		public NinePatchCommand RecordNinePatch(
			Vector2 position,
			Vector2 size,
			Texture texture,
			NinePatchInsets insets,
			Color? color = null,
			ShaderProgram shader = null
		)
		{
			return Add(new NinePatchCommand(position, size, texture, insets, color, shader));
		}

		public NinePatchCommand RecordNinePatch(
			float x,
			float y,
			float width,
			float height,
			Texture texture,
			NinePatchInsets insets,
			Color? color = null,
			ShaderProgram shader = null
		)
		{
			return RecordNinePatch(new Vector2(x, y), new Vector2(width, height), texture, insets, color, shader);
		}

		public RoundedRectangleCommand RecordRoundedRectangle(
			Vector2 position,
			Vector2 size,
			CornerRadii radii,
			float thickness = 1,
			Color? color = null,
			int cornerSegments = 0
		)
		{
			return Add(new RoundedRectangleCommand(position, size, radii, thickness, color, cornerSegments));
		}

		public RoundedRectangleCommand RecordRoundedRectangle(
			float x,
			float y,
			float width,
			float height,
			CornerRadii radii,
			float thickness = 1,
			Color? color = null,
			int cornerSegments = 0
		)
		{
			return RecordRoundedRectangle(
				new Vector2(x, y),
				new Vector2(width, height),
				radii,
				thickness,
				color,
				cornerSegments
			);
		}

		public FilledRoundedRectangleCommand RecordFilledRoundedRectangle(
			Vector2 position,
			Vector2 size,
			CornerRadii radii,
			Color? color = null,
			int cornerSegments = 0
		)
		{
			return Add(new FilledRoundedRectangleCommand(position, size, radii, color, cornerSegments));
		}

		public FilledRoundedRectangleCommand RecordFilledRoundedRectangle(
			float x,
			float y,
			float width,
			float height,
			CornerRadii radii,
			Color? color = null,
			int cornerSegments = 0
		)
		{
			return RecordFilledRoundedRectangle(
				new Vector2(x, y),
				new Vector2(width, height),
				radii,
				color,
				cornerSegments
			);
		}

		public TexturedRoundedRectangleCommand RecordTexturedRoundedRectangle(
			Vector2 position,
			Vector2 size,
			CornerRadii radii,
			Texture texture,
			Vector2 uvMin,
			Vector2 uvMax,
			Color? color = null,
			ShaderProgram shader = null,
			int cornerSegments = 0
		)
		{
			return Add(
				new TexturedRoundedRectangleCommand(
					position,
					size,
					radii,
					texture,
					uvMin,
					uvMax,
					color,
					shader,
					cornerSegments
				)
			);
		}

		public TexturedRoundedRectangleCommand RecordTexturedRoundedRectangle(
			float x,
			float y,
			float width,
			float height,
			CornerRadii radii,
			Texture texture,
			float u0 = 0,
			float v0 = 0,
			float u1 = 1,
			float v1 = 1,
			Color? color = null,
			ShaderProgram shader = null,
			int cornerSegments = 0
		)
		{
			return RecordTexturedRoundedRectangle(
				new Vector2(x, y),
				new Vector2(width, height),
				radii,
				texture,
				new Vector2(u0, v0),
				new Vector2(u1, v1),
				color,
				shader,
				cornerSegments
			);
		}

		public EllipseCommand RecordEllipse(
			Vector2 center,
			Vector2 radii,
			float thickness = 1,
			Color? color = null,
			int segments = 0
		)
		{
			return Add(new EllipseCommand(center, radii, thickness, color, segments));
		}

		public EllipseCommand RecordCircle(
			Vector2 center,
			float radius,
			float thickness = 1,
			Color? color = null,
			int segments = 0
		)
		{
			return RecordEllipse(center, new Vector2(radius), thickness, color, segments);
		}

		public FilledEllipseCommand RecordFilledEllipse(
			Vector2 center,
			Vector2 radii,
			Color? color = null,
			int segments = 0
		)
		{
			return Add(new FilledEllipseCommand(center, radii, color, segments));
		}

		public FilledEllipseCommand RecordFilledCircle(
			Vector2 center,
			float radius,
			Color? color = null,
			int segments = 0
		)
		{
			return RecordFilledEllipse(center, new Vector2(radius), color, segments);
		}

		public TexturedEllipseCommand RecordTexturedEllipse(
			Vector2 center,
			Vector2 radii,
			Texture texture,
			float u0 = 0,
			float v0 = 0,
			float u1 = 1,
			float v1 = 1,
			Color? color = null,
			ShaderProgram shader = null,
			int segments = 0
		)
		{
			return RecordTexturedEllipse(
				center,
				radii,
				texture,
				new Vector2(u0, v0),
				new Vector2(u1, v1),
				color,
				shader,
				segments
			);
		}

		public TexturedEllipseCommand RecordTexturedEllipse(
			Vector2 center,
			Vector2 radii,
			Texture texture,
			Vector2 uvMin,
			Vector2 uvMax,
			Color? color = null,
			ShaderProgram shader = null,
			int segments = 0
		)
		{
			return Add(new TexturedEllipseCommand(center, radii, texture, uvMin, uvMax, color, shader, segments));
		}

		public TexturedEllipseCommand RecordTexturedCircle(
			Vector2 center,
			float radius,
			Texture texture,
			float u0 = 0,
			float v0 = 0,
			float u1 = 1,
			float v1 = 1,
			Color? color = null,
			ShaderProgram shader = null,
			int segments = 0
		)
		{
			return RecordTexturedCircle(
				center,
				radius,
				texture,
				new Vector2(u0, v0),
				new Vector2(u1, v1),
				color,
				shader,
				segments
			);
		}

		public TexturedEllipseCommand RecordTexturedCircle(
			Vector2 center,
			float radius,
			Texture texture,
			Vector2 uvMin,
			Vector2 uvMax,
			Color? color = null,
			ShaderProgram shader = null,
			int segments = 0
		)
		{
			return RecordTexturedEllipse(
				center,
				new Vector2(radius),
				texture,
				uvMin,
				uvMax,
				color,
				shader,
				segments
			);
		}

		public RingCommand RecordRing(
			Vector2 center,
			float innerRadius,
			float outerRadius,
			float startAngle,
			float endAngle,
			Color? color = null,
			int segments = 0
		)
		{
			return Add(new RingCommand(center, innerRadius, outerRadius, startAngle, endAngle, color, segments));
		}

		public RingCommand RecordRing(
			Vector2 center,
			float innerRadius,
			float outerRadius,
			Color? color = null,
			int segments = 0
		)
		{
			return RecordRing(center, innerRadius, outerRadius, 0, MathF.Tau, color, segments);
		}

		public RingLinesCommand RecordRingLines(
			Vector2 center,
			float innerRadius,
			float outerRadius,
			float startAngle,
			float endAngle,
			float thickness = 1,
			Color? color = null,
			int segments = 0
		)
		{
			return Add(
				new RingLinesCommand(
					center,
					innerRadius,
					outerRadius,
					startAngle,
					endAngle,
					thickness,
					color,
					segments
				)
			);
		}

		public RingLinesCommand RecordRingLines(
			Vector2 center,
			float innerRadius,
			float outerRadius,
			float thickness = 1,
			Color? color = null,
			int segments = 0
		)
		{
			return RecordRingLines(
				center,
				innerRadius,
				outerRadius,
				0,
				MathF.Tau,
				thickness,
				color,
				segments
			);
		}

		public QuadraticBezierCommand RecordQuadraticBezier(
			Vector2 start,
			Vector2 control,
			Vector2 end,
			float thickness = 1,
			Color? color = null,
			int segments = 0
		)
		{
			return Add(new QuadraticBezierCommand(start, control, end, thickness, color, segments));
		}

		public CubicBezierCommand RecordCubicBezier(
			Vector2 start,
			Vector2 control1,
			Vector2 control2,
			Vector2 end,
			float thickness = 1,
			Color? color = null,
			int segments = 0
		)
		{
			return Add(new CubicBezierCommand(start, control1, control2, end, thickness, color, segments));
		}

		public DrawTextCommand RecordDrawText(
			GfxFont font,
			Vector2 position,
			string text,
			Color color,
			float fontSize = -1,
			bool debugDraw = false
		)
		{
			return Add(new DrawTextCommand(font, position, text, color, fontSize, debugDraw));
		}

		public DrawMesh3DCommand RecordDrawMesh(
			Mesh3D mesh,
			Texture texture = null,
			ShaderProgram shader = null
		)
		{
			return Add(new DrawMesh3DCommand(mesh, texture, shader));
		}

		public DrawRenderModelCommand RecordDrawModel(RenderModel model, ShaderProgram shader = null)
		{
			return Add(new DrawRenderModelCommand(model, shader));
		}

		private void EnsureMutable()
		{
			if (IsExecuting)
				throw new InvalidOperationException("A command list cannot be modified while it is executing.");
		}
	}
}
