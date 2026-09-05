using System.Net;
using System.Security.Cryptography;
using Voxelgine.Engine;
using Voxelgine.Engine.Server;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class HardeningRegressionTests
{
	[Theory]
	[InlineData(1024)]
	[InlineData(1025)]
	[InlineData(10000)]
	public void LargeFarmKeepsGrowthTimingAndRemovesMaturePlants(int count)
	{
		ChunkMap map = new(); WorldObjectStore store = new();
		for (int i = 0; i < count; i++) map.SetBlock(i % 100, 0, i / 100, BlockType.WetFarmland);
		store.Restore(Enumerable.Range(0, count).Select(i => Plant((ulong)i + 1, i % 100, i / 100)));
		using FarmingService farming = new(map, store);
		int largest = 0;
		store.ColumnDelta += (_, delta) => largest = Math.Max(largest, delta.Operations.Count);
		farming.Update(1);
		Assert.Equal(count, farming.GrowingPlantCount);
		Assert.All(store.GetAll(), plant => Assert.Equal(FarmingService.WheatGrowthIncrement, plant.GrowthProgress));
		farming.Update(118);
		Assert.All(store.GetAll(), plant => Assert.False(plant.IsMature));
		farming.Update(1);
		Assert.Equal(0, farming.GrowingPlantCount);
		Assert.All(store.GetAll(), plant => Assert.True(plant.IsMature));
		Assert.InRange(largest, 1, 1024);
	}

	[Theory]
	[InlineData(0, false)]
	[InlineData(1, false)]
	[InlineData(10, true)]
	public void TransferCommitsBothStoresOrNeither(int capacity, bool expected)
	{
		ContainerInventory source = new(1); source.Restore(new[] { new ItemStack(ItemIds.Wheat, 10) });
		PlayerInventory target = new();
		ushort maximum = ItemCatalog.Get(ItemIds.Wheat).MaximumStack;
		ItemStack[] slots = Enumerable.Repeat(new ItemStack(ItemIds.Wheat, maximum), PlayerInventory.SlotCount).ToArray();
		slots[0] = new ItemStack(ItemIds.Wheat, (ushort)(maximum - capacity)); target.Restore(slots, default, -1);
		long sourceRevision = source.Revision, targetRevision = target.Revision;
		Assert.Equal(expected, new InventoryTransactionService().TryTransfer(source, 0, target, 10));
		Assert.Equal((int)slots.Sum(s => s.Count) + 10, target.GetSlots().ToArray().Sum(s => s.Count) + source.GetSlot(0).Count);
		if (!expected) { Assert.Equal(sourceRevision, source.Revision); Assert.Equal(targetRevision, target.Revision); Assert.Equal(slots, target.GetSlots().ToArray()); }
	}

	[Fact]
	public void SnapshotsCannotRollbackOrStealAnotherColumnsIdentity()
	{
		WorldObjectStore store = new(); WorldPlantRecord first = Plant(1, 0, 0), second = Plant(2, 16, 0);
		store.InstallColumnSnapshot(0, 0, 1, 5, new[] { first });
		store.InstallColumnSnapshot(1, 0, 2, 5, new[] { second });
		int replacements = 0; store.ColumnReplaced += _ => replacements++;
		store.InstallColumnSnapshot(0, 0, 1, 3, Array.Empty<WorldPlantRecord>());
		store.InstallColumnSnapshot(0, 0, 1, 5, Array.Empty<WorldPlantRecord>());
		Assert.Equal(0, replacements); Assert.Equal(5, store.GetColumn(0, 0).Revision);
		Assert.Throws<InvalidDataException>(() => store.InstallColumnSnapshot(0, 0, 1, 6, new[] { second with { Support = first.Support } }));
		Assert.Equal(first, store.GetColumn(0, 0).Records.Single()); Assert.Equal(second, store.GetColumn(1, 0).Records.Single());
	}

	[Fact]
	public void DeltaRequiresAnInstalledSnapshotBeforeLocalColumnMetadataCanBeUsed()
	{
		WorldObjectStore store = new(); WorldPlantRecord plant = Plant(1, 0, 0);
		WorldObjectColumnState local = store.GetColumn(0, 0);
		WorldObjectOperation[] operations = { new(WorldObjectOperationKind.Upsert, plant, default) };
		Assert.False(store.TryApplyReplicatedDelta(0, 0, local.Epoch, 1, 2, operations));
		Assert.Equal(0, store.Count);
		store.InstallColumnSnapshot(0, 0, local.Epoch, 1, Array.Empty<WorldPlantRecord>());
		Assert.True(store.TryApplyReplicatedDelta(0, 0, local.Epoch, 1, 2, operations));
		store.InstallColumnSnapshot(0, 0, local.Epoch, 1, Array.Empty<WorldPlantRecord>());
		Assert.Equal(plant, store.GetColumn(0, 0).Records.Single());
	}

	[Fact]
	public void OverlayOnlyWritesChangedMaterialsAndRemovedPositions()
	{
		WorldPlantOverlay overlay = new(); WorldPlantRecord plant = Plant(1, 0, 0);
		WorldObjectColumnState column = new(0, 0, 1, 1, new[] { plant });
		List<(BlockCoordinate, ushort?)> writes = new();
		int Apply(WorldObjectColumnState value) => overlay.Apply(value, stage => (ushort)(100 + stage), (position, material) => writes.Add((position, material)));
		Assert.Equal(1, Apply(column)); writes.Clear();
		Assert.Equal(0, Apply(column with { Records = new[] { plant with { GrowthProgress = 547 } } })); Assert.Empty(writes);
		Assert.Equal(1, Apply(column with { Records = new[] { plant with { GrowthProgress = 8192 } } }));
		Assert.Equal(1, Apply(column with { Records = Array.Empty<WorldPlantRecord>() })); Assert.Null(writes.Last().Item2);
	}

	[Fact]
	public void CalendarSetTimeRetainsDayAndSurvivesRestore()
	{
		DayNightCycle clock = new(); clock.RestoreAbsoluteGameTime(75);
		clock.SetTime(20); Assert.Equal(92, clock.AbsoluteGameHours);
		DayNightCycle loaded = new(); loaded.RestoreAbsoluteGameTime(clock.AbsoluteGameHours); Assert.Equal(20, loaded.TimeOfDay);
		clock.SetTime(0); Assert.Equal(72, clock.AbsoluteGameHours);
	}

	[Fact]
	public void TransportDeadlinesRetainPrecisionAfterYears()
	{
		NetConnection connection = new(new IPEndPoint(IPAddress.Loopback, 1), 100_000_000d);
		Assert.Equal(0.015, connection.TimeSinceLastReceive(100_000_000.015), 7);
		SnapshotBuffer<int> snapshots = new(); snapshots.Add(1, 100_000_000d); snapshots.Add(2, 100_000_000.02);
		Assert.True(snapshots.Sample(100_000_000.01, out int from, out int to, out float fraction));
		Assert.Equal(1, from); Assert.Equal(2, to); Assert.InRange(fraction, .499f, .501f);
	}

	[Fact]
	public void DatagramBoundsAndFairRotationHold()
	{
		BoundedDatagramQueue queue = new(); IPEndPoint noisy = new(IPAddress.Loopback, 1), other = new(IPAddress.Loopback, 2);
		for (int i = 0; i < 4095; i++) Assert.True(queue.Enqueue(new byte[1], noisy));
		Assert.True(queue.Enqueue(new byte[1], other)); Assert.False(queue.Enqueue(new byte[1], noisy));
		var drained = queue.Drain(); Assert.Equal(65, drained.Count); Assert.Contains(drained, entry => entry.Sender.Equals(other));
		queue.Clear();
		for (int i = 0; i < 128; i++) Assert.True(queue.Enqueue(new byte[65536], noisy));
		Assert.False(queue.Enqueue(new byte[1], noisy)); Assert.Equal(8 * 1024 * 1024, queue.Bytes);
	}

	[Fact]
	public void IdentityPermissionsBansAndPinsSurviveRestart()
	{
		string root = Path.Combine(Path.GetTempPath(), "voxel-identity-" + Guid.NewGuid()); Directory.CreateDirectory(root);
		try
		{
			PlayerIdentity identity;
			using (NetworkIdentity first = NetworkIdentity.LoadOrCreate(Path.Combine(root, "client.pk8"))) identity = first.Identity;
			using NetworkIdentity reloaded = NetworkIdentity.LoadOrCreate(Path.Combine(root, "client.pk8")); Assert.Equal(identity, reloaded.Identity);
			ServerAccessPolicy access = new(Path.Combine(root, "access.json")); Assert.False(access.IsAdministrator(identity));
			access.SetAdministrator(identity, true); access.SetBanned(identity, true);
			access = new(Path.Combine(root, "access.json")); Assert.True(access.IsBanned(identity)); Assert.False(access.IsAdministrator(identity));
			access.SetBanned(identity, false); Assert.True(access.IsAdministrator(identity));
			ServerTrustStore trust = new(Path.Combine(root, "trust.json")); trust.Confirm("localhost:1", identity);
			using NetworkIdentity changed = new(); trust = new(Path.Combine(root, "trust.json"));
			Assert.False(trust.Matches("localhost:1", changed.Identity)); Assert.Throws<InvalidOperationException>(() => trust.Confirm("localhost:1", changed.Identity));
			Assert.True(trust.Reset("localhost:1")); trust.Confirm("localhost:1", changed.Identity);
		}
		finally { Directory.Delete(root, true); }
	}

	[Fact]
	public void AuthenticationRejectsTamperingReplayReflectionAndOtherSessions()
	{
		byte[] secret = RandomNumberGenerator.GetBytes(32), transcript = RandomNumberGenerator.GetBytes(64);
		using AuthenticatedDatagrams client = new(secret, transcript, false), server = new(secret, transcript, true), other = new(secret, new byte[64], true);
		byte[] packet = client.Protect(new byte[] { 2, 1, 2, 3 }); // Includes an ACK-shaped payload.
		for (int index = 0; index < packet.Length; index++)
		{
			byte[] tampered = (byte[])packet.Clone(); tampered[index] ^= 1; Assert.False(server.TryUnprotect(tampered, out _));
		}
		Assert.False(client.TryUnprotect(packet, out _)); Assert.False(other.TryUnprotect(packet, out _));
		Assert.True(server.TryUnprotect(packet, out byte[] clear)); Assert.Equal(new byte[] { 2, 1, 2, 3 }, clear);
		Assert.False(server.TryUnprotect(packet, out _));
		byte[] older = client.Protect(new byte[] { 5 }), newer = client.Protect(new byte[] { 6 });
		Assert.True(server.TryUnprotect(newer, out _)); Assert.True(server.TryUnprotect(older, out _));
	}

	[Fact]
	public void ProofIsBoundToTranscriptAndClientRejectsInvalidServerProof()
	{
		using NetworkIdentity identity = new(), server = new(); using ClientHandshake client = new(identity, "Player");
		byte[] proof = identity.Sign(client.Hello); byte[] changed = (byte[])client.Hello.Clone(); changed[^1] ^= 1;
		Assert.True(NetworkIdentity.Verify(identity.PublicKey, client.Hello, proof)); Assert.False(NetworkIdentity.Verify(identity.PublicKey, changed, proof));
		using ECDiffieHellman ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
		byte[] challenge = ConnectionHandshake.Encode(ConnectionHandshake.Challenge, client.Nonce, server.PublicKey, ephemeral.ExportSubjectPublicKeyInfo(), new byte[32], new byte[64]);
		Assert.Throws<CryptographicException>(() => client.AcceptChallenge(challenge, new(), "server:1"));
	}

	private static WorldPlantRecord Plant(ulong id, int x, int z) => new(PersistentWorldObjectKey.Placed(new PersistentWorldObjectId(id)), WorldPlantType.Wheat, 0, 255, ItemIds.Wheat, new(x, 0, z));
}
