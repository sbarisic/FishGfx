using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace FishGfx.Voxels
{
	public readonly struct VoxelTextureRegion
	{
		public VoxelTextureRegion(int x, int y, int width, int height, int atlasWidth, int atlasHeight)
		{
			if (x < 0)
				throw new ArgumentOutOfRangeException(nameof(x));
			if (y < 0)
				throw new ArgumentOutOfRangeException(nameof(y));
			if (width <= 0)
				throw new ArgumentOutOfRangeException(nameof(width));
			if (height <= 0)
				throw new ArgumentOutOfRangeException(nameof(height));
			if (atlasWidth <= 0 || x + width > atlasWidth)
				throw new ArgumentOutOfRangeException(nameof(atlasWidth));
			if (atlasHeight <= 0 || y + height > atlasHeight)
				throw new ArgumentOutOfRangeException(nameof(atlasHeight));

			X = x;
			Y = y;
			Width = width;
			Height = height;
			AtlasWidth = atlasWidth;
			AtlasHeight = atlasHeight;
		}

		public int X { get; }
		public int Y { get; }
		public int Width { get; }
		public int Height { get; }
		public int AtlasWidth { get; }
		public int AtlasHeight { get; }

		public Vector2 Map(Vector2 sourceUv)
		{
			if (!IsFinite(sourceUv))
				throw new ArgumentOutOfRangeException(nameof(sourceUv));

			return new Vector2(
				(X + sourceUv.X * Width) / AtlasWidth,
				1 - (Y + sourceUv.Y * Height) / AtlasHeight
			);
		}

		private static bool IsFinite(Vector2 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y);
		}
	}

	public sealed class VoxelModel
	{
		private readonly VoxelVertex[] vertices;
		private readonly ReadOnlyCollection<VoxelVertex> readOnlyVertices;

		public VoxelModel(IEnumerable<VoxelVertex> vertices)
		{
			if (vertices == null)
				throw new ArgumentNullException(nameof(vertices));

			this.vertices = new List<VoxelVertex>(vertices).ToArray();

			if (this.vertices.Length == 0 || this.vertices.Length % 3 != 0)
				throw new ArgumentException("Voxel models must contain a non-empty triangle list.", nameof(vertices));

			Vector3[] positions = new Vector3[this.vertices.Length];

			for (int i = 0; i < this.vertices.Length; i++)
			{
				VoxelVertex vertex = this.vertices[i];

				if (!IsFinite(vertex.Position) || !IsFinite(vertex.Normal) || !IsFinite(vertex.UV))
					throw new ArgumentException("Voxel model vertices must contain only finite values.", nameof(vertices));
				if (vertex.Normal.LengthSquared() <= 0)
					throw new ArgumentException("Voxel model normals cannot be zero.", nameof(vertices));

				vertex.Normal = Vector3.Normalize(vertex.Normal);
				this.vertices[i] = vertex;
				positions[i] = vertex.Position;
			}

			Bounds = AABB.CalculateAABB(positions);
			readOnlyVertices = Array.AsReadOnly(this.vertices);
		}

		public IReadOnlyList<VoxelVertex> Vertices => readOnlyVertices;
		public AABB Bounds { get; }
		internal VoxelVertex[] VertexArray => vertices;

		private static bool IsFinite(Vector2 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y);
		}

		private static bool IsFinite(Vector3 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
		}
	}

	public sealed class VoxelModelSet
	{
		private readonly VoxelModel[] models;
		private readonly ReadOnlyCollection<VoxelModel> readOnlyModels;

		public VoxelModelSet(params VoxelModel[] models)
		{
			if (models == null)
				throw new ArgumentNullException(nameof(models));
			if (models.Length == 0)
				throw new ArgumentException("A voxel model set must contain at least one model.", nameof(models));

			this.models = (VoxelModel[])models.Clone();

			for (int i = 0; i < this.models.Length; i++)
				if (this.models[i] == null)
					throw new ArgumentException("Voxel model sets cannot contain null models.", nameof(models));

			readOnlyModels = Array.AsReadOnly(this.models);
		}

		public IReadOnlyList<VoxelModel> Models => readOnlyModels;

		public VoxelModel Select(int worldX, int worldY, int worldZ)
		{
			if (models.Length == 1)
				return models[0];

			unchecked
			{
				int hash = worldX * 73856093 ^ worldY * 19349663 ^ worldZ * 83492791;
				int index = (hash & int.MaxValue) % models.Length;
				return models[index];
			}
		}
	}

	public static class MinecraftVoxelModelLoader
	{
		private static readonly int[] TriangleOrder = { 0, 1, 2, 3, 0, 2 };

		public static VoxelModel LoadFile(
			string path,
			IReadOnlyDictionary<string, VoxelTextureRegion> textureRegions
		)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A model path is required.", nameof(path));

			return Load(File.ReadAllText(path), textureRegions);
		}

		public static VoxelModel Load(
			byte[] json,
			IReadOnlyDictionary<string, VoxelTextureRegion> textureRegions
		)
		{
			if (json == null)
				throw new ArgumentNullException(nameof(json));

			return Load(Encoding.UTF8.GetString(json), textureRegions);
		}

		public static VoxelModel Load(
			string json,
			IReadOnlyDictionary<string, VoxelTextureRegion> textureRegions
		)
		{
			if (json == null)
				throw new ArgumentNullException(nameof(json));
			if (textureRegions == null)
				throw new ArgumentNullException(nameof(textureRegions));

			try
			{
				using JsonDocument document = JsonDocument.Parse(json);
				JsonElement root = document.RootElement;

				if (!root.TryGetProperty("elements", out JsonElement elements) || elements.ValueKind != JsonValueKind.Array)
					throw new FormatException("Minecraft voxel models require an elements array.");

				(int Width, int Height)? textureSize = ReadTextureSize(root);
				List<VoxelVertex> vertices = new List<VoxelVertex>();

				foreach (JsonElement element in elements.EnumerateArray())
					AppendElement(element, textureRegions, textureSize, vertices);

				return new VoxelModel(vertices);
			}
			catch (JsonException exception)
			{
				throw new FormatException("The Minecraft voxel model JSON is invalid.", exception);
			}
		}

		private static void AppendElement(
			JsonElement element,
			IReadOnlyDictionary<string, VoxelTextureRegion> regions,
			(int Width, int Height)? textureSize,
			List<VoxelVertex> destination
		)
		{
			Vector3 from = ReadVector3(element, "from") / 16;
			Vector3 to = ReadVector3(element, "to") / 16;

			if (to.X < from.X || to.Y < from.Y || to.Z < from.Z)
				throw new FormatException("Model element bounds must be ordered from minimum to maximum.");

			ReadRotation(element, out Vector3 origin, out Matrix4x4 rotation);

			if (!element.TryGetProperty("faces", out JsonElement faces) || faces.ValueKind != JsonValueKind.Object)
				throw new FormatException("Every model element requires a faces object.");

			foreach (JsonProperty faceProperty in faces.EnumerateObject())
			{
				GetFace(faceProperty.Name, from, to, out Vector3 normal, out Vector3[] corners, out Vector2[] cornerUvs);
				JsonElement face = faceProperty.Value;
				Vector4 sourceUv = ReadVector4(face, "uv");

				ValidateSourceUv(sourceUv);

				Vector4 uv = sourceUv / 16;
				int faceRotation = ReadFaceRotation(face);

				if (!face.TryGetProperty("texture", out JsonElement textureElement))
					throw new FormatException("Every model face requires a texture reference.");

				string textureKey = textureElement.GetString()?.TrimStart('#');

				if (string.IsNullOrWhiteSpace(textureKey) || !regions.TryGetValue(textureKey, out VoxelTextureRegion region))
					throw new FormatException($"Model face references unresolved texture '{textureKey}'.");

				ValidateTextureRegion(textureKey, region, textureSize);

				for (int i = 0; i < TriangleOrder.Length; i++)
				{
					int cornerIndex = TriangleOrder[i];
					Vector3 position = TransformAround(corners[cornerIndex], origin, rotation);
					Vector3 transformedNormal = Vector3.Normalize(Vector3.TransformNormal(normal, rotation));
					Vector2 localUv = RotateFaceUv(cornerUvs[cornerIndex], faceRotation);
					Vector2 faceUv = new Vector2(
						float.Lerp(uv.X, uv.Z, localUv.X),
						float.Lerp(uv.Y, uv.W, localUv.Y)
					);
					destination.Add(new VoxelVertex(position, Color.White, region.Map(faceUv), transformedNormal));
				}
			}
		}

		private static (int Width, int Height)? ReadTextureSize(JsonElement root)
		{
			if (!root.TryGetProperty("texture_size", out JsonElement value))
				return null;
			if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
				throw new FormatException("Model texture_size must contain exactly two integer dimensions.");
			if (!value[0].TryGetInt32(out int width) || !value[1].TryGetInt32(out int height))
				throw new FormatException("Model texture_size dimensions must be integers.");
			if (width <= 0 || height <= 0)
				throw new FormatException("Model texture_size dimensions must be positive.");

			return (width, height);
		}

		private static void ValidateTextureRegion(
			string textureKey,
			VoxelTextureRegion region,
			(int Width, int Height)? textureSize
		)
		{
			if (!textureSize.HasValue)
				return;
			if (region.Width != textureSize.Value.Width || region.Height != textureSize.Value.Height)
			{
				throw new FormatException(
					$"Model texture '#{textureKey}' requires a {textureSize.Value.Width}x{textureSize.Value.Height} "
					+ $"region, but the supplied region is {region.Width}x{region.Height}."
				);
			}
		}

		private static void ValidateSourceUv(Vector4 uv)
		{
			if (
				uv.X < 0 || uv.X > 16
				|| uv.Y < 0 || uv.Y > 16
				|| uv.Z < 0 || uv.Z > 16
				|| uv.W < 0 || uv.W > 16
			)
			{
				throw new FormatException("Model face UV coordinates must remain within the logical 0..16 texture region.");
			}
		}

		private static int ReadFaceRotation(JsonElement face)
		{
			if (!face.TryGetProperty("rotation", out JsonElement rotationElement))
				return 0;
			if (rotationElement.ValueKind != JsonValueKind.Number || !rotationElement.TryGetInt32(out int rotation))
				throw new FormatException("Model face rotation must be an integer number of degrees.");
			if (rotation != 0 && rotation != 90 && rotation != 180 && rotation != 270)
				throw new FormatException("Model face rotation must be 0, 90, 180, or 270 degrees.");

			return rotation;
		}

		private static Vector2 RotateFaceUv(Vector2 uv, int rotation)
		{
			return rotation switch
			{
				0 => uv,
				90 => new Vector2(uv.Y, 1 - uv.X),
				180 => Vector2.One - uv,
				270 => new Vector2(1 - uv.Y, uv.X),
				_ => throw new ArgumentOutOfRangeException(nameof(rotation)),
			};
		}

		private static void ReadRotation(JsonElement element, out Vector3 origin, out Matrix4x4 rotation)
		{
			origin = Vector3.Zero;
			rotation = Matrix4x4.Identity;

			if (!element.TryGetProperty("rotation", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
				return;

			if (value.TryGetProperty("origin", out _))
				origin = ReadVector3(value, "origin") / 16;

			Vector3 degrees = Vector3.Zero;

			if (value.TryGetProperty("axis", out JsonElement axisElement))
			{
				float angle = ReadFiniteSingle(value, "angle", required: true);
				degrees = axisElement.GetString()?.ToLowerInvariant() switch
				{
					"x" => new Vector3(angle, 0, 0),
					"y" => new Vector3(0, angle, 0),
					"z" => new Vector3(0, 0, angle),
					_ => throw new FormatException("Model rotation axes must be x, y, or z."),
				};
			}
			else
			{
				degrees = new Vector3(
					ReadFiniteSingle(value, "x", required: false),
					ReadFiniteSingle(value, "y", required: false),
					ReadFiniteSingle(value, "z", required: false)
				);
			}

			const float DegreesToRadians = MathF.PI / 180;
			rotation = Matrix4x4.CreateRotationX(degrees.X * DegreesToRadians)
				* Matrix4x4.CreateRotationY(degrees.Y * DegreesToRadians)
				* Matrix4x4.CreateRotationZ(degrees.Z * DegreesToRadians);
		}

		private static Vector3 TransformAround(Vector3 value, Vector3 origin, Matrix4x4 rotation)
		{
			return Vector3.Transform(value - origin, rotation) + origin;
		}

		private static void GetFace(
			string name,
			Vector3 min,
			Vector3 max,
			out Vector3 normal,
			out Vector3[] corners,
			out Vector2[] uvs
		)
		{
			uvs = new[] { new Vector2(1, 0), Vector2.Zero, new Vector2(0, 1), Vector2.One };

			switch (name.ToLowerInvariant())
			{
				case "east":
					normal = Vector3.UnitX;
					corners = new[]
					{
						new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z),
						new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, min.Y, min.Z),
					};
					break;
				case "west":
					normal = -Vector3.UnitX;
					corners = new[]
					{
						new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, max.Y, min.Z),
						new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, min.Y, max.Z),
					};
					break;
				case "up":
					normal = Vector3.UnitY;
					corners = new[]
					{
						new Vector3(max.X, max.Y, min.Z), new Vector3(min.X, max.Y, min.Z),
						new Vector3(min.X, max.Y, max.Z), new Vector3(max.X, max.Y, max.Z),
					};
					break;
				case "down":
					normal = -Vector3.UnitY;
					corners = new[]
					{
						new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z),
						new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z),
					};
					break;
				case "south":
					normal = Vector3.UnitZ;
					corners = new[]
					{
						new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z),
						new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, min.Y, max.Z),
					};
					uvs = new[] { Vector2.One, new Vector2(1, 0), Vector2.Zero, new Vector2(0, 1) };
					break;
				case "north":
					normal = -Vector3.UnitZ;
					corners = new[]
					{
						new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, min.Y, min.Z),
						new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z),
					};
					uvs = new[] { Vector2.Zero, new Vector2(0, 1), Vector2.One, new Vector2(1, 0) };
					break;
				default:
					throw new FormatException($"Unsupported model face direction '{name}'.");
			}
		}

		private static Vector3 ReadVector3(JsonElement owner, string name)
		{
			JsonElement array = RequireArray(owner, name, 3);
			return new Vector3(ReadFiniteSingle(array[0]), ReadFiniteSingle(array[1]), ReadFiniteSingle(array[2]));
		}

		private static Vector4 ReadVector4(JsonElement owner, string name)
		{
			JsonElement array = RequireArray(owner, name, 4);
			return new Vector4(
				ReadFiniteSingle(array[0]),
				ReadFiniteSingle(array[1]),
				ReadFiniteSingle(array[2]),
				ReadFiniteSingle(array[3])
			);
		}

		private static JsonElement RequireArray(JsonElement owner, string name, int length)
		{
			if (!owner.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
				throw new FormatException($"Model property '{name}' must be an array.");
			if (value.GetArrayLength() != length)
				throw new FormatException($"Model property '{name}' must contain exactly {length} values.");

			return value;
		}

		private static float ReadFiniteSingle(JsonElement owner, string name, bool required)
		{
			if (!owner.TryGetProperty(name, out JsonElement value))
			{
				if (required)
					throw new FormatException($"Model property '{name}' is required.");

				return 0;
			}

			return ReadFiniteSingle(value);
		}

		private static float ReadFiniteSingle(JsonElement value)
		{
			if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out float result) || !float.IsFinite(result))
				throw new FormatException("Model numeric values must be finite numbers.");

			return result;
		}
	}
}
