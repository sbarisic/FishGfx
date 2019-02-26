using FishGfx.Graphics;
using Glfw3;
using OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx {
	public static class RenderAPI {
		public static string Version {
			get {
				return Internal_OpenGL.Version;
			}
		}

		public static string[] Extensions {
			get {
				return Internal_OpenGL.Extensions;
			}
		}

		public static string Renderer {
			get;
			internal set;
		}

		static Queue<GraphicsObject> GCQueue = new Queue<GraphicsObject>();

		internal static void EnqueueCollection(GraphicsObject Obj) {
			lock (GCQueue) {
				GCQueue.Enqueue(Obj);
			}
		}

		public static void CollectGarbage() {
			lock (GCQueue) {
				while (GCQueue.Count > 0) {
					/*GraphicsObject Obj = GCQueue.Dequeue();
					Console.WriteLine("~{0}({1})", Obj.GetType().Name, Obj.ID);
					Obj.GraphicsDispose();*/

					GCQueue.Dequeue().GraphicsDispose();
				}
			}
		}

		public static void GetDesktopResolution(out int Width, out int Height) {
			Internal_OpenGL.InitGLFW();
			Glfw.Monitor Monitor = Glfw.GetPrimaryMonitor();

			Glfw.VideoMode VideoMode = Glfw.GetVideoMode(Monitor);
			Width = VideoMode.Width;
			Height = VideoMode.Height;
		}

		public static void DbgPushGroup(string Name) {
			Gl.PushDebugGroup(DebugSource.DebugSourceApplication, 0, Name.Length, Name);
		}

		public static void DbgPopGroup() {
			Gl.PopDebugGroup();
		}

		public static void DbgMessage(string Msg) {
			Gl.DebugMessageInsert(DebugSource.DebugSourceApplication, DebugType.DebugTypeOther, 0, DebugSeverity.DebugSeverityLow, Msg.Length, Msg);
		}

		public static void DbgMessage(string Fmt, params object[] Args) {
			DbgMessage(string.Format(Fmt, Args));
		}
	}
}
