using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using FishGfx.Formats;
using FishGfx.Graphics;

namespace FishGfx.FishUI
{
	/// <summary>FishUI graphics backend that draws into a caller-owned FishGfx render pass.</summary>
	public sealed class FishGfxFishUIGraphics : global::FishUI.SimpleFishUIGfx, IDisposable
	{
		private sealed class ImageResource : IDisposable
		{
			internal ImageResource(Texture texture, Bitmap bitmap)
			{
				Texture = texture;
				Bitmap = bitmap;
			}

			internal Texture Texture { get; }
			internal Bitmap Bitmap { get; }

			public void Dispose()
			{
				Texture.Dispose();
				Bitmap.Dispose();
			}
		}

		private sealed class BindingScope : IDisposable
		{
			private FishGfxFishUIGraphics owner;

			internal BindingScope(FishGfxFishUIGraphics owner) => this.owner = owner;

			public void Dispose()
			{
				FishGfxFishUIGraphics current = owner;
				owner = null;
				current?.ReleaseBinding();
			}
		}

		private readonly RenderWindow window;
		private readonly GraphicsContext context;
		private readonly RootedFishUIFileSystem fileSystem;
		private readonly Dictionary<string, ImageResource> images = new Dictionary<string, ImageResource>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, TTFFont> fonts = new Dictionary<string, TTFFont>(StringComparer.OrdinalIgnoreCase);
		private RenderPass pass;
		private RenderView renderView;
		private RenderState renderState;
		private IDisposable stateScope;
		private IDisposable viewScope;
		private IDisposable scissorScope;
		private bool drawing;
		private bool disposed;

		public FishGfxFishUIGraphics(RenderWindow window, string resourceRoot = null)
		{
			this.window = window ?? throw new ArgumentNullException(nameof(window));
			context = window.Graphics;
			fileSystem = new RootedFishUIFileSystem(resourceRoot);
		}

		/// <summary>Root used to resolve relative FishUI fonts, images, themes, and layouts.</summary>
		public string ResourceRoot => fileSystem.RootDirectory;

		/// <summary>Number of primitive FishGfx draw operations emitted by the last FishUI frame.</summary>
		public int LastFrameDrawCallCount { get; private set; }

		/// <summary>File-system adapter rooted at <see cref="ResourceRoot"/>.</summary>
		public RootedFishUIFileSystem FileSystem => fileSystem;

		/// <summary>
		/// Binds FishUI drawing to an active render pass. Call <c>FishUI.TickDraw</c> before disposing the returned scope.
		/// </summary>
		public IDisposable UseRenderPass(RenderPass renderPass, RenderView view, RenderState state)
		{
			ThrowIfDisposed();
			if (renderPass == null)
				throw new ArgumentNullException(nameof(renderPass));
			if (pass != null)
				throw new InvalidOperationException("A FishUI render pass is already bound.");
			pass = renderPass;
			renderView = view;
			renderState = state;
			return new BindingScope(this);
		}

		public override void Init() => ThrowIfDisposed();
		public override int GetWindowWidth() => window.WindowWidth;
		public override int GetWindowHeight() => window.WindowHeight;
		public override void FocusWindow() => window.Focus();

		public override void BeginDrawing(float deltaTime)
		{
			ThrowIfDisposed();
			if (pass == null)
				throw new InvalidOperationException("UseRenderPass must bind an active pass before FishUI draws.");
			if (drawing)
				throw new InvalidOperationException("FishUI drawing has already begun.");

			LastFrameDrawCallCount = 0;
			stateScope = pass.PushState(renderState);
			try
			{
				viewScope = pass.PushView(renderView);
				drawing = true;
			}
			catch
			{
				stateScope.Dispose();
				stateScope = null;
				throw;
			}
		}

		public override void EndDrawing()
		{
			if (!drawing)
				return;
			try
			{
				scissorScope?.Dispose();
				scissorScope = null;
			}
			finally
			{
				try
				{
					viewScope?.Dispose();
					viewScope = null;
				}
				finally
				{
					stateScope?.Dispose();
					stateScope = null;
					drawing = false;
				}
			}
		}

		public override void BeginScissor(Vector2 position, Vector2 size)
		{
			RenderPass current = RequireDrawing();
			Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(position, size, GetWindowHeight());
			RenderState clipped = renderState;
			clipped.EnableScissorTest = true;
			clipped.ScissorRegion = new AABB(converted, size);
			scissorScope?.Dispose();
			scissorScope = current.PushState(clipped);
		}

		public override void EndScissor()
		{
			scissorScope?.Dispose();
			scissorScope = null;
		}

		public override global::FishUI.FontRef LoadFont(
			string fileName,
			float size,
			float spacing,
			global::FishUI.FishColor color
		)
		{
			return LoadFont(fileName, size, spacing, color, global::FishUI.FontStyle.Regular);
		}

		public override global::FishUI.FontRef LoadFont(
			string fileName,
			float size,
			float spacing,
			global::FishUI.FishColor color,
			global::FishUI.FontStyle style
		)
		{
			ThrowIfDisposed();
			if (!float.IsFinite(size) || size <= 0)
				throw new ArgumentOutOfRangeException(nameof(size));
			if (!float.IsFinite(spacing))
				throw new ArgumentOutOfRangeException(nameof(spacing));
			string path = fileSystem.ResolvePath(fileName);
			if (!fonts.TryGetValue(path, out TTFFont font))
			{
				font = new TTFFont(path);
				fonts.Add(path, font);
			}
			float lineHeight = font.LineHeight * size / font.FontSize;
			return new global::FishUI.FontRef
			{
				Path = fileName,
				Userdata = font,
				Size = size,
				Spacing = spacing,
				Color = color,
				Style = style,
				LineHeight = lineHeight,
				IsMonospaced = IsMonospaced(font, size),
			};
		}

		public override global::FishUI.ImageRef LoadImage(string fileName)
		{
			ThrowIfDisposed();
			string path = fileSystem.ResolvePath(fileName);
			if (!images.TryGetValue(path, out ImageResource resource))
			{
				using Image loaded = Image.FromFile(path);
				Bitmap bitmap = new Bitmap(loaded);
				try
				{
					Texture texture = TextureLoader.FromImage(context, bitmap, new TextureLoadOptions
					{
						Sampling = new TextureSamplingState(TextureFilter.Linear, TextureFilter.Linear),
					});
					resource = new ImageResource(texture, bitmap);
					images.Add(path, resource);
				}
				catch
				{
					bitmap.Dispose();
					throw;
				}
			}

			return new global::FishUI.ImageRef
			{
				Path = fileName,
				Width = resource.Texture.Width,
				Height = resource.Texture.Height,
				Userdata = resource,
				Userdata2 = resource.Bitmap,
			};
		}

		public override global::FishUI.FishColor GetImageColor(global::FishUI.ImageRef image, Vector2 position)
		{
			ImageResource resource = GetImageResource(image);
			(Vector2 sourcePosition, Vector2 sourceSize) = GetSourceRegion(image);
			int x = checked((int)position.X + (int)sourcePosition.X);
			int y = checked((int)position.Y + (int)sourcePosition.Y);
			if (position.X < 0 || position.Y < 0 || position.X >= sourceSize.X || position.Y >= sourceSize.Y)
				throw new ArgumentOutOfRangeException(nameof(position));
			System.Drawing.Color color = resource.Bitmap.GetPixel(x, y);
			return new global::FishUI.FishColor(color.R, color.G, color.B, color.A);
		}

		public override void SetImageFilter(global::FishUI.ImageRef image, bool pixelated)
		{
			ImageResource resource = GetImageResource(image);
			TextureFilter filter = pixelated ? TextureFilter.Nearest : TextureFilter.Linear;
			resource.Texture.SetSampling(new TextureSamplingState(filter, filter));
		}

		public override Vector2 MeasureText(global::FishUI.FontRef fontReference, string text)
		{
			if (string.IsNullOrEmpty(text))
				return Vector2.Zero;
			TTFFont font = GetFont(fontReference);
			float previousSize = font.ScaledFontSize;
			font.ScaledFontSize = fontReference.Size;
			try
			{
				Vector2 measured = font.MeasureString(text);
				if (fontReference.Spacing != 0)
					measured.X += Math.Max(0, LongestLineLength(text) - 1) * fontReference.Spacing;
				return measured;
			}
			finally { font.ScaledFontSize = previousSize; }
		}

		public override global::FishUI.FishUIFontMetrics GetFontMetrics(global::FishUI.FontRef fontReference)
		{
			TTFFont font = GetFont(fontReference);
			float lineHeight = font.LineHeight * fontReference.Size / font.FontSize;
			float ascent = lineHeight * 0.8f;
			return new global::FishUI.FishUIFontMetrics(
				lineHeight,
				ascent,
				lineHeight - ascent,
				ascent,
				MeasureText(fontReference, "x").X,
				MeasureText(fontReference, "W").X
			);
		}

		public override void DrawLine(Vector2 position1, Vector2 position2, float thickness, global::FishUI.FishColor color)
		{
			RenderPass current = RequireDrawing();
			current.Line(
				new Vertex2(FishUIConversions.ToFishGfxPoint(position1, GetWindowHeight()), FishUIConversions.ToFishGfxColor(color)),
				new Vertex2(FishUIConversions.ToFishGfxPoint(position2, GetWindowHeight()), FishUIConversions.ToFishGfxColor(color)),
				thickness
			);
			LastFrameDrawCallCount++;
		}

		public override void DrawRectangle(Vector2 position, Vector2 size, global::FishUI.FishColor color)
		{
			Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(position, size, GetWindowHeight());
			RequireDrawing().FilledRectangle(converted.X, converted.Y, size.X, size.Y, FishUIConversions.ToFishGfxColor(color));
			LastFrameDrawCallCount++;
		}

		public override void DrawRectangleOutline(Vector2 position, Vector2 size, global::FishUI.FishColor color)
		{
			Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(position, size, GetWindowHeight());
			RequireDrawing().Rectangle(converted.X, converted.Y, size.X, size.Y, 1, FishUIConversions.ToFishGfxColor(color));
			LastFrameDrawCallCount++;
		}

		public override void DrawCircle(Vector2 center, float radius, global::FishUI.FishColor color)
		{
			RequireDrawing().FilledCircle(
				FishUIConversions.ToFishGfxPoint(center, GetWindowHeight()),
				radius,
				FishUIConversions.ToFishGfxColor(color)
			);
			LastFrameDrawCallCount++;
		}

		public override void DrawCircleOutline(Vector2 center, float radius, global::FishUI.FishColor color, float thickness = 1)
		{
			RequireDrawing().Circle(
				FishUIConversions.ToFishGfxPoint(center, GetWindowHeight()),
				radius,
				thickness,
				FishUIConversions.ToFishGfxColor(color)
			);
			LastFrameDrawCallCount++;
		}

		public override void DrawImage(
			global::FishUI.ImageRef image,
			Vector2 position,
			float rotation,
			float scale,
			global::FishUI.FishColor color
		)
		{
			DrawImage(image, position, new Vector2(image.Width, image.Height), rotation, scale, color);
		}

		public override void DrawImage(
			global::FishUI.ImageRef image,
			Vector2 position,
			Vector2 size,
			float rotation,
			float scale,
			global::FishUI.FishColor color
		)
		{
			if (!float.IsFinite(scale) || scale < 0)
				throw new ArgumentOutOfRangeException(nameof(scale));
			Vector2 destinationSize = size * scale;
			(Vector2 sourcePosition, Vector2 sourceSize) = GetSourceRegion(image);
			DrawImageRegionCore(image, sourcePosition, sourceSize, position, destinationSize, rotation, color);
		}

		protected override void DrawImageRegion(
			global::FishUI.ImageRef image,
			Vector2 sourcePosition,
			Vector2 sourceSize,
			Vector2 destinationPosition,
			Vector2 destinationSize,
			global::FishUI.FishColor color
		)
		{
			(Vector2 basePosition, Vector2 ignored) = GetSourceRegion(image);
			DrawImageRegionCore(image, basePosition + sourcePosition, sourceSize, destinationPosition, destinationSize, 0, color);
		}

		public override void DrawNPatch(
			global::FishUI.NPatch patch,
			Vector2 position,
			Vector2 size,
			global::FishUI.FishColor color
		)
		{
			base.DrawNPatch(patch, position, size, color, 0);
		}

		public override void DrawNPatch(
			global::FishUI.NPatch patch,
			Vector2 position,
			Vector2 size,
			global::FishUI.FishColor color,
			float rotation
		)
		{
			if (rotation == 0)
			{
				base.DrawNPatch(patch, position, size, color, 0);
				return;
			}

			Vector2 center = FishUIConversions.ToFishGfxPoint(position + size / 2, GetWindowHeight());
			Matrix4x4 transform = CreateScreenRotation(center, rotation);
			using (RequireDrawing().PushModel(transform))
				base.DrawNPatch(patch, position, size, color, 0);
		}

		public override void DrawTextColorScale(
			global::FishUI.FontRef fontReference,
			string text,
			Vector2 position,
			global::FishUI.FishColor color,
			float scale
		)
		{
			if (string.IsNullOrEmpty(text))
				return;
			if (!float.IsFinite(scale) || scale <= 0)
				throw new ArgumentOutOfRangeException(nameof(scale));
			TTFFont font = GetFont(fontReference);
			Vector2 measured = MeasureText(fontReference, text) * scale;
			Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(position, measured, GetWindowHeight());
			RequireDrawing().DrawText(
				font,
				converted,
				text,
				FishUIConversions.ToFishGfxColor(color),
				fontReference.Size * scale
			);
			LastFrameDrawCallCount++;
		}

		private void DrawImageRegionCore(
			global::FishUI.ImageRef image,
			Vector2 sourcePosition,
			Vector2 sourceSize,
			Vector2 destinationPosition,
			Vector2 destinationSize,
			float rotation,
			global::FishUI.FishColor color
		)
		{
			ImageResource resource = GetImageResource(image);
			(Vector2 uvMinimum, Vector2 uvMaximum) = FishUIConversions.ToAtlasUv(
				sourcePosition,
				sourceSize,
				resource.Texture.Width,
				resource.Texture.Height
			);
			Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(destinationPosition, destinationSize, GetWindowHeight());
			RenderPass current = RequireDrawing();
			if (rotation == 0)
			{
				current.TexturedRectangle(
					converted.X,
					converted.Y,
					destinationSize.X,
					destinationSize.Y,
					uvMinimum.X,
					uvMinimum.Y,
					uvMaximum.X,
					uvMaximum.Y,
					FishUIConversions.ToFishGfxColor(color),
					resource.Texture
				);
			}
			else
			{
				Vector2 anchor = FishUIConversions.ToFishGfxPoint(destinationPosition, GetWindowHeight());
				using (current.PushModel(CreateScreenRotation(anchor, rotation)))
					current.TexturedRectangle(
						converted.X,
						converted.Y,
						destinationSize.X,
						destinationSize.Y,
						uvMinimum.X,
						uvMinimum.Y,
						uvMaximum.X,
						uvMaximum.Y,
						FishUIConversions.ToFishGfxColor(color),
						resource.Texture
					);
			}
			LastFrameDrawCallCount++;
		}

		private static Matrix4x4 CreateScreenRotation(Vector2 anchor, float degrees)
		{
			if (!float.IsFinite(degrees))
				throw new ArgumentOutOfRangeException(nameof(degrees));
			return Matrix4x4.CreateTranslation(-anchor.X, -anchor.Y, 0)
				* Matrix4x4.CreateRotationZ(-degrees * MathF.PI / 180)
				* Matrix4x4.CreateTranslation(anchor.X, anchor.Y, 0);
		}

		private static (Vector2 Position, Vector2 Size) GetSourceRegion(global::FishUI.ImageRef image)
		{
			if (image == null)
				throw new ArgumentNullException(nameof(image));
			if (!image.IsAtlasRegion)
				return (Vector2.Zero, new Vector2(image.Width, image.Height));

			int x = image.SourceX;
			int y = image.SourceY;
			global::FishUI.ImageRef parent = image.AtlasParent;
			while (parent != null && parent.IsAtlasRegion)
			{
				x += parent.SourceX;
				y += parent.SourceY;
				parent = parent.AtlasParent;
			}
			return (new Vector2(x, y), new Vector2(image.SourceW, image.SourceH));
		}

		private static ImageResource GetImageResource(global::FishUI.ImageRef image)
		{
			if (image?.Userdata is ImageResource resource)
				return resource;
			throw new ArgumentException("The image was not loaded by this FishGfx FishUI backend.", nameof(image));
		}

		private static TTFFont GetFont(global::FishUI.FontRef fontReference)
		{
			if (fontReference?.Userdata is TTFFont font)
				return font;
			throw new ArgumentException("The font was not loaded by this FishGfx FishUI backend.", nameof(fontReference));
		}

		private static bool IsMonospaced(TTFFont font, float size)
		{
			float previousSize = font.ScaledFontSize;
			font.ScaledFontSize = size;
			try { return Math.Abs(font.MeasureString("W").X - font.MeasureString("i").X) < 0.5f; }
			finally { font.ScaledFontSize = previousSize; }
		}

		private static int LongestLineLength(string text)
		{
			int maximum = 0;
			int current = 0;
			for (int index = 0; index < text.Length; index++)
			{
				if (text[index] == '\n')
				{
					maximum = Math.Max(maximum, current);
					current = 0;
				}
				else if (text[index] != '\r')
					current++;
			}
			return Math.Max(maximum, current);
		}

		private RenderPass RequireDrawing()
		{
			ThrowIfDisposed();
			if (!drawing || pass == null)
				throw new InvalidOperationException("FishUI drawing is not active.");
			return pass;
		}

		private void ReleaseBinding()
		{
			if (drawing)
				EndDrawing();
			pass = null;
			renderView = default;
			renderState = default;
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(FishGfxFishUIGraphics));
		}

		public void Dispose()
		{
			if (disposed)
				return;
			if (drawing)
				EndDrawing();
			pass = null;
			foreach (TTFFont font in fonts.Values)
				font.Dispose();
			fonts.Clear();
			foreach (ImageResource image in images.Values)
				image.Dispose();
			images.Clear();
			disposed = true;
		}
	}
}
