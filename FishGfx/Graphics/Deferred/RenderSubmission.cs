using System;
using System.Numerics;

namespace FishGfx.Graphics
{
	/// <summary>
	/// An immutable command-batch submission with captured entity transform and sorting metadata.
	/// </summary>
	public sealed class RenderSubmission
	{
		internal RenderSubmission(
			GraphicsCommandBatch batch,
			RenderBucket bucket,
			Matrix4x4 model,
			Vector3 sortPosition,
			int layer,
			ulong sortKey,
			object tag,
			long sequence
		)
		{
			Batch = batch;
			Bucket = bucket;
			Model = model;
			SortPosition = sortPosition;
			Layer = layer;
			SortKey = sortKey;
			Tag = tag;
			Sequence = sequence;
		}

		public GraphicsCommandBatch Batch { get; }
		public RenderBucket Bucket { get; }
		public Matrix4x4 Model { get; }
		public Vector3 SortPosition { get; }
		public int Layer { get; }
		public ulong SortKey { get; }
		public object Tag { get; }
		public long Sequence { get; }

		public void Execute(RenderPass pass)
		{
			if (pass == null) throw new ArgumentNullException(nameof(pass));
			using (pass.PushModel(Model)) Batch.Execute(pass);
		}

		public void Execute()
		{
			ShaderUniforms uniforms = ShaderUniforms.Current;
			Matrix4x4 previousModel = uniforms.Model;

			uniforms.Model = Model;

			try
			{
				Batch.Execute();
			}
			finally
			{
				uniforms.Model = previousModel;
			}
		}
	}
}
