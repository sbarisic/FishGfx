using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics
{
	public enum QueryTgt
	{
		TimeElapsed = 35007,
		SamplesPassed = 35092,
		AnySamplesPassed = 35887,
		PrimitivesGenerated = 35975,
		TransformFeedbackPrimitivesWritten = 35976,
		AnySamplesPassedConservative = 36202,
	}

	public class OcclusionQuery : GraphicsObject
	{
		static OcclusionQuery legacyCurrentQuery;
		public static OcclusionQuery CurrentQuery
		{
			get => GraphicsContext.CurrentOrNull?.ActiveQuery ?? legacyCurrentQuery;
			set
			{
				if (GraphicsContext.CurrentOrNull != null)
					GraphicsContext.CurrentOrNull.ActiveQuery = value;
				else
					legacyCurrentQuery = value;
			}
		}

		public bool IsOcclusionTest;
		public QueryTgt QueryTarget;

		public OcclusionQuery(QueryTgt Target)
		{
			IsOcclusionTest = false;
			QueryTarget = Target;
			ID = Internal_OpenGL.Is45OrAbove ? Internal_OpenGL.GL.CreateQuery((QueryTarget)QueryTarget) : Internal_OpenGL.GL.GenQuery();
		}

		public override void Bind()
		{
			EnsureCurrentOwner();
			if (CurrentQuery != null)
				throw new InvalidOperationException("Only one occlusion query can be active at a time.");
			CurrentQuery = this;
			try { Internal_OpenGL.GL.BeginQuery((QueryTarget)QueryTarget, ID); }
			catch { CurrentQuery = null; throw; }
		}

		public override void Unbind()
		{
			EnsureCurrentOwner();
			if (!ReferenceEquals(CurrentQuery, this))
				throw new InvalidOperationException("This occlusion query is not active.");
			try { Internal_OpenGL.GL.EndQuery((QueryTarget)QueryTarget); }
			finally { CurrentQuery = null; }
		}

		public void BeginConditional(bool Wait = true)
		{
			EnsureCurrentOwner();
			Internal_OpenGL.GL.BeginConditionalRender(ID, Wait ? GLEnum.QueryByRegionWait : GLEnum.QueryByRegionNoWait);
		}

		public void EndConditional()
		{
			EnsureCurrentOwner();
			Internal_OpenGL.GL.EndConditionalRender();
		}

		public override void GraphicsDispose()
		{
			Internal_OpenGL.GL.DeleteQueries(ID);
		}
	}
}
