using System;
using System.Runtime.CompilerServices;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public sealed class GraphicsResourceLifetimeTests
{
	[Fact]
	public void RegistrationDoesNotKeepResourceWrapperAlive()
	{
		(
			GraphicsResourceRegistration registration,
			WeakReference resourceReference
		) = CreateUnrootedRegistration();

		for (int attempt = 0; attempt < 5 && resourceReference.IsAlive; attempt++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}

		Assert.False(resourceReference.IsAlive);
		GC.KeepAlive(registration);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (
		GraphicsResourceRegistration Registration,
		WeakReference ResourceReference
	) CreateUnrootedRegistration()
	{
		GraphicsContext owner = (GraphicsContext)
			RuntimeHelpers.GetUninitializedObject(typeof(GraphicsContext));
		GraphicsBuffer resource = (GraphicsBuffer)
			RuntimeHelpers.GetUninitializedObject(typeof(GraphicsBuffer));

		GC.SuppressFinalize(resource);

		GraphicsResourceRegistration registration =
			GraphicsResourceRegistration.Create(owner, resource);
		WeakReference resourceReference = new(resource);

		return (registration, resourceReference);
	}
}
