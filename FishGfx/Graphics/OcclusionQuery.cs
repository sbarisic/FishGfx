using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Graphics {
	public enum QueryTgt {
		TimeElapsed = 35007,
		SamplesPassed = 35092,
		AnySamplesPassed = 35887,
		PrimitivesGenerated = 35975,
		TransformFeedbackPrimitivesWritten = 35976,
		AnySamplesPassedConservative = 36202
	}

	public class OcclusionQuery : GraphicsObject {
		public static OcclusionQuery CurrentQuery;

		public bool IsOcclusionTest;
		public QueryTgt QueryTarget;
		OcclusionQuery ResetQuery;

		public OcclusionQuery(QueryTgt Target) {
			IsOcclusionTest = false;
			QueryTarget = Target;
			ID = Internal_OpenGL.GL.CreateQuery((QueryTarget)QueryTarget);
		}

		public override void Bind() {
			ResetQuery = CurrentQuery;
			CurrentQuery = this;
			Internal_OpenGL.GL.BeginQuery((QueryTarget)QueryTarget, ID);
		}

		public override void Unbind() {
			CurrentQuery = ResetQuery;
			Internal_OpenGL.GL.EndQuery((QueryTarget)QueryTarget);
		}

		public void BeginConditional(bool Wait = true) {
			Internal_OpenGL.GL.BeginConditionalRender(ID, Wait ? GLEnum.QueryByRegionWait : GLEnum.QueryByRegionNoWait);
		}

		public void EndConditional() {
			Internal_OpenGL.GL.EndConditionalRender();
		}

		public override void GraphicsDispose() {
			Internal_OpenGL.GL.DeleteQueries(ID);
		}
	}
}
