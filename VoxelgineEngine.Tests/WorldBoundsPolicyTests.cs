using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Server;

namespace VoxelgineEngine.Tests;

public sealed class WorldBoundsPolicyTests
{
	[Theory]
	[InlineData(-50, false)]
	[InlineData(-49.999f, false)]
	[InlineData(-50.001f, true)]
	public void VoidBoundaryMatchesPlayerDeathRule(float y, bool expected)
	{
		Assert.Equal(expected, WorldBoundsPolicy.IsBelowVoid(new Vector3(0, y, 0)));
	}

	[Fact]
	public void KillFallenNpcs_KillsOnlyNpcsBelowTheVoidBoundary()
	{
		VEntNPC fallen = new()
		{
			Position = new Vector3(1, -51, 2),
			Health = 175,
			MaxHealth = 100,
		};
		VEntNPC safe = new()
		{
			Position = new Vector3(1, WorldBoundsPolicy.VoidThreshold, 2),
		};
		VEntPickup fallenPickup = new()
		{
			Position = new Vector3(1, -100, 2),
		};
		VoxEntity[] entities = [safe, fallenPickup, fallen];

		VEntNPC[] removed = WorldBoundsPolicy.KillFallenNpcs(entities);

		Assert.Equal([fallen], removed);
		Assert.True(fallen.IsDead);
		Assert.False(safe.IsDead);
	}
}
