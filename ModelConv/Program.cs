using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;

namespace ModelConv {
	enum ModelFormat {
		Obj,
		Foam,
		Smd
	}

	class Program {
		static void Main(string[] Args) {
			if (Args.Length != 2) {
				Console.WriteLine("ModelConv.exe path/to/input/model path/to/output/model");
				Console.WriteLine("Supported formats:");

				string[] OutFormatNames = Enum.GetNames(typeof(ModelFormat));
				foreach (var OutFormatName in OutFormatNames)
					Console.WriteLine("\t.{0}", OutFormatName.ToLower());
				return;
			}


			string InExt = Path.GetExtension(Args[0]).ToLower();
			string OutExt = Path.GetExtension(Args[1]).ToLower();

			if (InExt.Length >= 1 && Enum.TryParse(InExt.Substring(1), true, out ModelFormat InFmt))
				if (OutExt.Length >= 1 && Enum.TryParse(OutExt.Substring(1), true, out ModelFormat OutFmt))
					ConvertModel(InFmt, OutFmt, Args[0], Args[1]);
				else
					Console.WriteLine("Unknown output format " + OutExt);
			else
				Console.WriteLine("Unknown input format " + InExt);
		}

		static void ConvertModel(ModelFormat InFmt, ModelFormat OutFmt, string InFile, string OutFile) {
			if (!File.Exists(InFile)) {
				Console.WriteLine("Input file not found `{0}´", InFile);
				return;
			}

			Console.WriteLine("{0} -> {1}", Path.GetFileName(InFile), Path.GetFileName(OutFile));

			if (File.Exists(OutFile))
				File.Delete(OutFile);

			string OutDirectoryName = new FileInfo(OutFile).Directory.FullName;
			if (!Directory.Exists(OutDirectoryName))
				Directory.CreateDirectory(OutDirectoryName);

			List<GenericMesh> Meshes = new List<GenericMesh>();

			if (InFmt == ModelFormat.Obj)
				Meshes.AddRange(Obj.Load(InFile));

			/*else if (InFmt == ModelFormat.Foam)
				InputVerts = Foam.Load(InFile);*/

			else if (InFmt == ModelFormat.Smd)
				Meshes.AddRange(Smd.Load(InFile));
			else
				throw new NotImplementedException();

			/*if (OutFmt == ModelFormat.Foam)
				Foam.Save(OutFile, InputVerts);
			else*/

			if (OutFmt == ModelFormat.Obj)
				Obj.Save(OutFile, Meshes);
			else if (OutFmt == ModelFormat.Smd)
				Smd.Save(OutFile, Meshes);
			else
				throw new NotImplementedException();
		}
	}
}
