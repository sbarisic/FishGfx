using System;
using System.IO;

namespace FishGfx.FishUI
{
	/// <summary>FishUI file-system adapter that resolves relative assets from a fixed root.</summary>
	public sealed class RootedFishUIFileSystem : global::FishUI.IFishUIFileSystem
	{
		public RootedFishUIFileSystem(string rootDirectory = null)
		{
			RootDirectory = Path.GetFullPath(rootDirectory ?? AppContext.BaseDirectory);
		}

		public string RootDirectory { get; }

		public string ResolvePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A path is required.", nameof(path));
			return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(RootDirectory, path));
		}

		public bool Exists(string path) => File.Exists(ResolvePath(path));
		public string ReadAllText(string path) => File.ReadAllText(ResolvePath(path));
		public void WriteAllText(string path, string contents) => File.WriteAllText(ResolvePath(path), contents);
		public string GetFullPath(string path) => ResolvePath(path);
		public string GetDirectoryName(string path) => Path.GetDirectoryName(path);
		public string CombinePath(string path1, string path2) => Path.Combine(path1, path2);
		public string GetFileName(string path) => Path.GetFileName(path);
		public string[] GetDirectories(string path)
		{
			try { return Directory.GetDirectories(ResolvePath(path)); }
			catch (IOException) { return Array.Empty<string>(); }
			catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
		}

		public string[] GetFiles(string path, string searchPattern = "*")
		{
			try { return Directory.GetFiles(ResolvePath(path), searchPattern); }
			catch (IOException) { return Array.Empty<string>(); }
			catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
		}

		public bool IsDirectory(string path) => Directory.Exists(ResolvePath(path));
		public string GetParentDirectory(string path) => Directory.GetParent(ResolvePath(path))?.FullName;
	}
}
