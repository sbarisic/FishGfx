using OpenGL;
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
			ID = Gl.CreateQuery((QueryTarget)QueryTarget);
		}

		public override void Bind() {
			ResetQuery = CurrentQuery;
			CurrentQuery = this;
			Gl.BeginQuery((QueryTarget)QueryTarget, ID);
		}

		public override void Unbind() {
			CurrentQuery = ResetQuery;
			Gl.EndQuery((QueryTarget)QueryTarget);
		}

		public void BeginConditional(bool Wait = true) {
			Gl.BeginConditionalRender(ID, Wait ? ConditionalQueryMode.QueryByRegionWait : ConditionalQueryMode.QueryByRegionNoWait);
		}

		public void EndConditional() {
			Gl.EndConditionalRender();
		}

		public override void GraphicsDispose() {
			Gl.DeleteQueries(ID);
		}
	}
}
