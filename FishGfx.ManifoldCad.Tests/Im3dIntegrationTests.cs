using System.Numerics;
using FishGfx.Cad;
using FishGfx.Im3d;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class Im3dIntegrationTests
{
	[Fact]
	public void QuaternionMatrixRoundTripNormalizesAndPreservesHemisphere()
	{
		Im3dPose input = new(
			new Vector3(12, -4, 31),
			new Quaternion(0.25f, -0.5f, 0.125f, 0.8f));

		Im3dPose output = Im3dContext.RoundTripForTest(input);

		Assert.InRange(output.Rotation.Length(), 0.99999f, 1.00001f);
		Assert.True(Quaternion.Dot(Quaternion.Normalize(input.Rotation), output.Rotation) > 0.9999f);
		Assert.Equal(input.Position, output.Position);
	}

	[Fact]
	public void CadFramePoseRoundTripPreservesTangentNormalAndOrigin()
	{
		CadFrame input = new(
			new CadPoint3(77.75, -22, 15),
			new CadPoint3(-1, 0, 0),
			new CadPoint3(0, 0, 1));

		Im3dPose bridgePose = Im3dContext.RoundTripForTest(CadViewport.ToIm3dPose(input));
		CadFrame output = CadViewport.ToCadFrame(bridgePose);

		AssertPoint(input.Origin, output.Origin);
		AssertPoint(input.Tangent, output.Tangent);
		AssertPoint(input.Normal, output.Normal);
		AssertPoint(input.Binormal, output.Binormal);
	}

	[Fact]
	public void WorldAxisRollAndReaimHaveExpectedTangentSemantics()
	{
		Vector3 tangent = -Vector3.UnitX;
		Quaternion xRoll = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.75f);
		Quaternion yReaim = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.75f);

		AssertVector(tangent, Vector3.Transform(tangent, xRoll));
		Assert.True(Vector3.Distance(tangent, Vector3.Transform(tangent, yReaim)) > 0.1f);
	}

	[Fact]
	public void ActiveToInactiveTransitionCommitsExactlyOnceAndCanBeSuppressed()
	{
		Im3dInteractionLifecycle lifecycle = new();
		Assert.Null(lifecycle.ObserveAfterEndFrame(42));
		Assert.Null(lifecycle.ObserveAfterEndFrame(42));
		Im3dReleaseTransition release = lifecycle.ObserveAfterEndFrame(0).Value;
		Assert.Equal(42u, release.Id);
		Assert.False(release.Suppressed);
		Assert.Null(lifecycle.ObserveAfterEndFrame(0));

		Assert.Null(lifecycle.ObserveAfterEndFrame(99));
		lifecycle.SuppressRelease(99);
		Im3dReleaseTransition cancelled = lifecycle.ObserveAfterEndFrame(0).Value;
		Assert.Equal(99u, cancelled.Id);
		Assert.True(cancelled.Suppressed);
	}

	[Fact]
	public void PartDraftDoesNotMutateCommittedPartTransform()
	{
		CadPart part = new()
		{
			Transform = new CadTransform(
				new CadPoint3(1, 2, 3),
				CadQuaternion.FromEulerDegrees(new CadPoint3(10, 20, 30)))
		};
		PartTransformDraftState draft = new(part)
		{
			Transform = new CadTransform(
				new CadPoint3(11, 12, 13),
				CadQuaternion.FromEulerDegrees(new CadPoint3(40, 50, 60)))
		};

		Assert.Equal(new CadPoint3(1, 2, 3), part.Transform.Translation);
		Assert.Equal(part.Transform, draft.OriginalTransform);
		Assert.NotEqual(part.Transform, draft.Transform);
	}

	[Fact]
	public void PartDraftResolutionDoesNotTreatNullPartAndDraftAsAMatch()
	{
		Assert.Null(ManifoldCadApplication.ResolvePartDraftTransform(null, null));

		CadPart selectedPart = new()
		{
			Transform = new CadTransform(
				new CadPoint3(1, 2, 3),
				CadQuaternion.Identity)
		};
		PartTransformDraftState matchingDraft = new(selectedPart)
		{
			Transform = new CadTransform(
				new CadPoint3(4, 5, 6),
				CadQuaternion.Identity)
		};
		CadPart differentPart = new();

		Assert.Null(ManifoldCadApplication.ResolvePartDraftTransform(selectedPart, null));
		Assert.Null(ManifoldCadApplication.ResolvePartDraftTransform(null, matchingDraft));
		Assert.Null(ManifoldCadApplication.ResolvePartDraftTransform(differentPart, matchingDraft));
		Assert.Equal(
			matchingDraft.Transform,
			ManifoldCadApplication.ResolvePartDraftTransform(selectedPart, matchingDraft));
	}

	[Fact]
	public void DrawCopyPreservesOrderedCommandRanges()
	{
		using Im3dContext context = new();
		context.BeginFrame(new Im3dFrameInput(
			new Vector3(0, 0, 10),
			-Vector3.UnitZ,
			Vector3.UnitY,
			new Vector3(0, 0, 10),
			-Vector3.UnitZ,
			new Vector2(1280, 720),
			2 * MathF.Tan(MathF.PI / 6),
			1f / 60,
			false,
			false));
		context.ManipulatePose(
			Im3dId.FromGuid(Guid.NewGuid()),
			Im3dPoseOperation.Rotate,
			new Im3dPose(Vector3.Zero, Quaternion.Identity));

		(Im3dFrameData drawData, _) = context.EndFrame();

		Assert.NotEmpty(drawData.Vertices);
		Assert.NotEmpty(drawData.Commands);
		int previousEnd = 0;
		uint previousOrder = 0;
		for (int index = 0; index < drawData.Commands.Length; index++)
		{
			Im3dDrawCommand command = drawData.Commands[index];
			Assert.Equal(previousEnd, command.FirstVertex);
			Assert.Equal((uint)index, command.SourceOrder);
			Assert.True(index == 0 || command.SourceOrder > previousOrder);
			Assert.True(command.VertexCount > 0);
			previousEnd += command.VertexCount;
			previousOrder = command.SourceOrder;
		}
		Assert.Equal(drawData.Vertices.Length, previousEnd);
	}

	private static void AssertPoint(CadPoint3 expected, CadPoint3 actual)
	{
		Assert.InRange(Math.Abs(expected.X - actual.X), 0, 1e-5);
		Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 1e-5);
		Assert.InRange(Math.Abs(expected.Z - actual.Z), 0, 1e-5);
	}

	private static void AssertVector(Vector3 expected, Vector3 actual)
	{
		Assert.InRange(Vector3.Distance(expected, actual), 0, 1e-5f);
	}
}
