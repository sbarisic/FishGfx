using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Im3d;

public sealed class Im3dRenderer : IDisposable
{
	private readonly List<Mesh3D> commandMeshes = [];
	private readonly ShaderStage vertexStage;
	private readonly ShaderStage lineGeometryStage;
	private readonly ShaderStage pointGeometryStage;
	private readonly ShaderStage triangleFragmentStage;
	private readonly ShaderStage lineFragmentStage;
	private readonly ShaderStage pointFragmentStage;
	private readonly ShaderProgram triangleProgram;
	private readonly ShaderProgram lineProgram;
	private readonly ShaderProgram pointProgram;
	private readonly GraphicsContext graphics;
	private bool disposed;

	public Im3dRenderer(GraphicsContext graphics)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		vertexStage = graphics.CreateShaderStage(ShaderStageType.Vertex, VertexShader);
		lineGeometryStage = graphics.CreateShaderStage(ShaderStageType.Geometry, LineGeometryShader);
		pointGeometryStage = graphics.CreateShaderStage(ShaderStageType.Geometry, PointGeometryShader);
		triangleFragmentStage = graphics.CreateShaderStage(ShaderStageType.Fragment, TriangleFragmentShader);
		lineFragmentStage = graphics.CreateShaderStage(ShaderStageType.Fragment, LineFragmentShader);
		pointFragmentStage = graphics.CreateShaderStage(ShaderStageType.Fragment, PointFragmentShader);
		triangleProgram = graphics.CreateShaderProgram(vertexStage, triangleFragmentStage);
		lineProgram = graphics.CreateShaderProgram(vertexStage, lineGeometryStage, lineFragmentStage);
		pointProgram = graphics.CreateShaderProgram(vertexStage, pointGeometryStage, pointFragmentStage);
	}

	public void Draw(RenderPass pass, Im3dFrameData frame)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(pass);
		ArgumentNullException.ThrowIfNull(frame);
		if (frame.Commands.Length == 0)
		{
			return;
		}

		EnsureMeshCapacity(frame.Commands.Length);
		using IDisposable state = pass.PushState(pass.State with
		{
			CullMode = CullMode.None,
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
			BlendEnabled = true,
			SourceBlend = BlendFactor.SourceAlpha,
			DestinationBlend = BlendFactor.OneMinusSourceAlpha,
		});

		for (int commandIndex = 0; commandIndex < frame.Commands.Length; commandIndex++)
		{
			Im3dDrawCommand command = frame.Commands[commandIndex];
			ValidateCommand(command, frame.Vertices.Length);
			Vertex3[] vertices = new Vertex3[command.VertexCount];
			for (int index = 0; index < command.VertexCount; index++)
			{
				Im3dVertex source = frame.Vertices[command.FirstVertex + index];
				vertices[index] = new Vertex3(source.Position, new Vector2(source.Size, 0), source.Color);
			}

			Mesh3D mesh = commandMeshes[commandIndex];
			mesh.PrimitiveType = ToPrimitiveType(command.Primitive);
			mesh.SetVertices(vertices);
			pass.DrawMesh(mesh, shader: ProgramFor(command.Primitive));
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		foreach (Mesh3D mesh in commandMeshes)
		{
			mesh.Dispose();
		}
		commandMeshes.Clear();
		triangleProgram.Dispose();
		lineProgram.Dispose();
		pointProgram.Dispose();
		vertexStage.Dispose();
		lineGeometryStage.Dispose();
		pointGeometryStage.Dispose();
		triangleFragmentStage.Dispose();
		lineFragmentStage.Dispose();
		pointFragmentStage.Dispose();
	}

	private void EnsureMeshCapacity(int count)
	{
		while (commandMeshes.Count < count)
		{
			commandMeshes.Add(graphics.CreateMesh3D(BufferUsage.Stream));
		}
	}

	private ShaderProgram ProgramFor(Im3dPrimitive primitive) => primitive switch
	{
		Im3dPrimitive.Triangles => triangleProgram,
		Im3dPrimitive.Lines => lineProgram,
		Im3dPrimitive.Points => pointProgram,
		_ => throw new ArgumentOutOfRangeException(nameof(primitive)),
	};

	private static PrimitiveType ToPrimitiveType(Im3dPrimitive primitive) => primitive switch
	{
		Im3dPrimitive.Triangles => PrimitiveType.Triangles,
		Im3dPrimitive.Lines => PrimitiveType.Lines,
		Im3dPrimitive.Points => PrimitiveType.Points,
		_ => throw new ArgumentOutOfRangeException(nameof(primitive)),
	};

	private static void ValidateCommand(Im3dDrawCommand command, int vertexCount)
	{
		if (command.FirstVertex < 0 || command.VertexCount < 0 ||
			command.FirstVertex > vertexCount - command.VertexCount)
		{
			throw new InvalidOperationException("An im3d draw command references vertices outside its copied frame.");
		}
	}

	private const string VertexShader = """
		#version 400
		layout(location = 0) in vec3 aPosition;
		layout(location = 1) in vec4 aColor;
		layout(location = 2) in vec2 aUv;

		uniform mat4 uModel;
		uniform mat4 uView;
		uniform mat4 uProjection;

		out VertexData
		{
			noperspective float edgeDistance;
			noperspective float size;
			smooth vec4 color;
		} vData;

		void main()
		{
			const float antialiasing = 2.0;
			vData.size = max(aUv.x, antialiasing);
			vData.color = aColor;
			vData.color.a *= smoothstep(0.0, 1.0, aUv.x / antialiasing);
			gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
		}
		""";

	private const string LineGeometryShader = """
		#version 400
		layout(lines) in;
		layout(triangle_strip, max_vertices = 4) out;

		uniform vec2 uViewport;

		in VertexData
		{
			noperspective float edgeDistance;
			noperspective float size;
			smooth vec4 color;
		} vData[];

		out VertexData
		{
			noperspective float edgeDistance;
			noperspective float size;
			smooth vec4 color;
		} gData;

		void emitLineVertex(vec4 clip, vec2 ndc, vec2 tangent, float edge, int source)
		{
			gl_Position = vec4((ndc + tangent) * clip.w, clip.zw);
			gData.edgeDistance = edge;
			gData.size = vData[source].size;
			gData.color = vData[source].color;
			EmitVertex();
		}

		void main()
		{
			vec2 start = gl_in[0].gl_Position.xy / gl_in[0].gl_Position.w;
			vec2 finish = gl_in[1].gl_Position.xy / gl_in[1].gl_Position.w;
			vec2 direction = start - finish;
			if (dot(direction, direction) < 1.0e-12)
			{
				direction = vec2(1.0, 0.0);
			}
			direction = normalize(vec2(direction.x, direction.y * uViewport.y / uViewport.x));
			vec2 normal = vec2(-direction.y, direction.x);
			vec2 startTangent = normal * vData[0].size / uViewport;
			vec2 endTangent = normal * vData[1].size / uViewport;

			emitLineVertex(gl_in[0].gl_Position, start, -startTangent, -vData[0].size, 0);
			emitLineVertex(gl_in[0].gl_Position, start, startTangent, vData[0].size, 0);
			emitLineVertex(gl_in[1].gl_Position, finish, -endTangent, -vData[1].size, 1);
			emitLineVertex(gl_in[1].gl_Position, finish, endTangent, vData[1].size, 1);
			EndPrimitive();
		}
		""";

	private const string PointGeometryShader = """
		#version 400
		layout(points) in;
		layout(triangle_strip, max_vertices = 4) out;

		uniform vec2 uViewport;

		in VertexData
		{
			noperspective float edgeDistance;
			noperspective float size;
			smooth vec4 color;
		} vData[];

		out vec4 gColor;
		out vec2 gPointCoordinate;
		flat out float gPointSize;

		void emitPointVertex(vec2 coordinate)
		{
			vec2 center = gl_in[0].gl_Position.xy / gl_in[0].gl_Position.w;
			vec2 offset = coordinate * vData[0].size / uViewport;
			gl_Position = vec4((center + offset) * gl_in[0].gl_Position.w, gl_in[0].gl_Position.zw);
			gColor = vData[0].color;
			gPointCoordinate = coordinate;
			gPointSize = vData[0].size;
			EmitVertex();
		}

		void main()
		{
			emitPointVertex(vec2(-1.0, -1.0));
			emitPointVertex(vec2(-1.0, 1.0));
			emitPointVertex(vec2(1.0, -1.0));
			emitPointVertex(vec2(1.0, 1.0));
			EndPrimitive();
		}
		""";

	private const string TriangleFragmentShader = """
		#version 400
		in VertexData
		{
			noperspective float edgeDistance;
			noperspective float size;
			smooth vec4 color;
		} vData;
		layout(location = 0) out vec4 outColor;
		void main() { outColor = vData.color; }
		""";

	private const string LineFragmentShader = """
		#version 400
		in VertexData
		{
			noperspective float edgeDistance;
			noperspective float size;
			smooth vec4 color;
		} vData;
		layout(location = 0) out vec4 outColor;
		void main()
		{
			float distanceToEdge = abs(vData.edgeDistance) / vData.size;
			float alpha = smoothstep(1.0, 1.0 - (2.0 / vData.size), distanceToEdge);
			outColor = vec4(vData.color.rgb, vData.color.a * alpha);
		}
		""";

	private const string PointFragmentShader = """
		#version 400
		in vec4 gColor;
		in vec2 gPointCoordinate;
		flat in float gPointSize;
		layout(location = 0) out vec4 outColor;
		void main()
		{
			float radius = length(gPointCoordinate);
			float alpha = smoothstep(1.0, 1.0 - (2.0 / gPointSize), radius);
			outColor = vec4(gColor.rgb, gColor.a * alpha);
		}
		""";
}
