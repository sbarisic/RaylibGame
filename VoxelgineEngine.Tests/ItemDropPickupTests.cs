using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;

public sealed class ItemDropPickupTests
{
	[Fact]
	public void GroundedDropUsesPlayerFeetForPickupDistance()
	{
		using var player = new Player(new TestEngineRunner(), 0);
		player.SetPosition(new Vector3(515.9097f, 64.68042f, 521.0977f));
		var drop = new VEntItemDrop();
		drop.SetPosition(new Vector3(515.5f, 63.5f, 521.5f));

		Assert.True(Vector3.DistanceSquared(player.Position, drop.Position) > 1.25f * 1.25f);
		Assert.True(ServerLoop.GetDropPickupDistanceSquared(player, drop) <= 1.25f * 1.25f);
	}

	[Fact]
	public void GroundedDropOutsidePickupRadiusIsRejected()
	{
		using var player = new Player(new TestEngineRunner(), 0);
		player.SetPosition(new Vector3(0, Player.PlayerEyeOffset, 0));
		var drop = new VEntItemDrop();
		drop.SetPosition(new Vector3(1.26f, 0, 0));

		Assert.True(ServerLoop.GetDropPickupDistanceSquared(player, drop) > 1.25f * 1.25f);
	}

	private sealed class TestEngineRunner : IFishEngineRunner
	{
		public IFishLogging Logging { get; } = new NullLogging();
		public ILerpManager LerpManager { get; } = new LerpManager();
		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }
	}

	private sealed class NullLogging : IFishLogging
	{
		public void Init(bool IsServer = false) { }
		public void WriteLine(string message) { }
		public void ServerWriteLine(string message) { }
		public void ClientWriteLine(string message) { }
		public void ServerNetworkWriteLine(string message) { }
		public void ClientNetworkWriteLine(string message) { }
	}
}
