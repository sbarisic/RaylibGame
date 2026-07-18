using System.Numerics;
using Voxelgine.Engine;

namespace VoxelgineEngine.Tests;

public sealed class EntityWireContractTests
{
	[Fact]
	public void NpcSnapshotPreservesEmptyAnimationField()
	{
		VEntNPC npc = new()
		{
			Position = new Vector3(1f, 2f, 3f),
			Velocity = new Vector3(4f, 5f, 6f),
		};
		npc.SetLookDirection(Vector3.UnitX);
		npc.SetAnimationState(2);

		using MemoryStream stream = new();
		using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			npc.WriteSnapshot(writer);

		stream.Position = 0;
		using BinaryReader reader = new(stream);
		for (int index = 0; index < 6; index++)
			_ = reader.ReadSingle();
		_ = reader.ReadBoolean();
		_ = reader.ReadSingle();
		for (int index = 0; index < 3; index++)
			_ = reader.ReadSingle();

		Assert.Equal(string.Empty, reader.ReadString());
		Assert.Equal(stream.Length, stream.Position);
	}
}
