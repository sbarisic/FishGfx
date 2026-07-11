using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest
{
	internal static unsafe class GalleryScreenshot
	{
		private const int ThumbnailWidth = 640;
		private const int ThumbnailHeight = 360;

		internal static string FileNameForTitle(string title)
		{
			if (string.IsNullOrWhiteSpace(title))
				throw new ArgumentException("A scene title is required.", nameof(title));

			StringBuilder slug = new StringBuilder();
			bool separatorPending = false;

			foreach (char character in title.Trim())
			{
				if (char.IsLetterOrDigit(character))
				{
					if (separatorPending && slug.Length > 0)
						slug.Append('-');

					slug.Append(char.ToLowerInvariant(character));
					separatorPending = false;
				}
				else if (slug.Length > 0)
				{
					separatorPending = true;
				}
			}

			if (slug.Length == 0)
				throw new ArgumentException("The scene title does not contain a usable filename.", nameof(title));

			return slug + ".png";
		}

		internal static string[] FileNamesForTitles(IEnumerable<string> titles)
		{
			if (titles == null)
				throw new ArgumentNullException(nameof(titles));

			string[] fileNames = titles.Select(FileNameForTitle).ToArray();
			HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string fileName in fileNames)
				if (!unique.Add(fileName))
					throw new InvalidOperationException($"Multiple gallery scenes resolve to '{fileName}'.");

			return fileNames;
		}

		internal static string FindPicturesDirectory()
		{
			DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);

			while (directory != null)
			{
				if (File.Exists(Path.Combine(directory.FullName, "FishGfx.Modern.sln")))
					return Path.Combine(directory.FullName, "FishGfx", "pictures");

				directory = directory.Parent;
			}

			throw new DirectoryNotFoundException(
				"Could not locate FishGfx.Modern.sln while resolving the gallery screenshot directory."
			);
		}

		internal static void Capture(RenderWindow window, string directory, string fileName)
		{
			if (window == null)
				throw new ArgumentNullException(nameof(window));

			if (string.IsNullOrWhiteSpace(directory))
				throw new ArgumentException("A screenshot directory is required.", nameof(directory));

			if (string.IsNullOrWhiteSpace(fileName))
				throw new ArgumentException("A screenshot filename is required.", nameof(fileName));

			Directory.CreateDirectory(directory);
			window.ReadPixels();

			int width = window.WindowWidth;
			int height = window.WindowHeight;

			if (window.PixelData == null || window.PixelData.Length < width * height)
				throw new InvalidOperationException("The framebuffer read did not return enough pixels.");

			using Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			Rectangle bounds = new Rectangle(0, 0, width, height);
			BitmapData bitmapData = bitmap.LockBits(
				bounds,
				ImageLockMode.WriteOnly,
				System.Drawing.Imaging.PixelFormat.Format32bppArgb
			);

			try
			{
				fixed (FishGfx.Color* source = window.PixelData)
				{
					byte* destinationStart = (byte*)bitmapData.Scan0;

					for (int y = 0; y < height; y++)
					{
						FishGfx.Color* sourceRow = source + (height - y - 1) * width;
						byte* destinationRow = destinationStart + y * bitmapData.Stride;

						for (int x = 0; x < width; x++)
						{
							FishGfx.Color color = sourceRow[x];
							destinationRow[x * 4] = color.B;
							destinationRow[x * 4 + 1] = color.G;
							destinationRow[x * 4 + 2] = color.R;
							destinationRow[x * 4 + 3] = color.A;
						}
					}
				}
			}
			finally
			{
				bitmap.UnlockBits(bitmapData);
			}

			SavePngAtomic(bitmap, Path.Combine(directory, fileName));

			string thumbnailDirectory = Path.Combine(directory, "thumbnails");
			Directory.CreateDirectory(thumbnailDirectory);

			using Bitmap thumbnail = new Bitmap(
				ThumbnailWidth,
				ThumbnailHeight,
				System.Drawing.Imaging.PixelFormat.Format32bppArgb
			);
			using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(thumbnail))
			{
				graphics.CompositingMode = CompositingMode.SourceCopy;
				graphics.CompositingQuality = CompositingQuality.HighQuality;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
				graphics.SmoothingMode = SmoothingMode.HighQuality;
				graphics.DrawImage(
					bitmap,
					new Rectangle(0, 0, ThumbnailWidth, ThumbnailHeight),
					new Rectangle(0, 0, width, height),
					GraphicsUnit.Pixel
				);
			}

			SavePngAtomic(thumbnail, Path.Combine(thumbnailDirectory, fileName));
		}

		private static void SavePngAtomic(Bitmap bitmap, string destination)
		{
			string temporary = destination + ".tmp";

			try
			{
				if (File.Exists(temporary))
					File.Delete(temporary);

				bitmap.Save(temporary, ImageFormat.Png);
				File.Move(temporary, destination, true);
			}
			finally
			{
				if (File.Exists(temporary))
					File.Delete(temporary);
			}
		}
	}
}
