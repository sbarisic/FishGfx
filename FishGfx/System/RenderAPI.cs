using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Graphics;
using Glfw3;

namespace FishGfx.System {
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
	}
}
