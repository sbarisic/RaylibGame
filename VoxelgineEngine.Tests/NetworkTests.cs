using System;
using System.Collections.Generic;
using System.Numerics;
using Voxelgine.Engine;

namespace VoxelgineEngine.Tests {
	// ──────────────────────────────────────────────
	// Packet Serialization Round-Trip Tests
	// ──────────────────────────────────────────────
	public class PacketSerializationTests {
		[Fact]
		public void ConnectPacket_RoundTrip() {
			var original = new ConnectPacket {
				PlayerName = "TestPlayer",
				ProtocolVersion = 42,
			};

			byte[] data = original.Serialize();
			var deserialized = (ConnectPacket)Packet.Deserialize(data);

			Assert.Equal(original.PlayerName, deserialized.PlayerName);
			Assert.Equal(original.ProtocolVersion, deserialized.ProtocolVersion);
		}

		[Fact]
		public void ConnectAcceptPacket_RoundTrip() {
			var original = new ConnectAcceptPacket {
				PlayerId = 3,
				ServerTick = 12345,
				WorldSeed = 99999,
			};

			byte[] data = original.Serialize();
			var deserialized = (ConnectAcceptPacket)Packet.Deserialize(data);

			Assert.Equal(original.PlayerId, deserialized.PlayerId);
			Assert.Equal(original.ServerTick, deserialized.ServerTick);
			Assert.Equal(original.WorldSeed, deserialized.WorldSeed);
		}

		[Fact]
		public void ConnectRejectPacket_RoundTrip() {
			var original = new ConnectRejectPacket {
				Reason = "Server full",
			};

			byte[] data = original.Serialize();
			var deserialized = (ConnectRejectPacket)Packet.Deserialize(data);

			Assert.Equal(original.Reason, deserialized.Reason);
		}

		[Fact]
		public void PlayerSnapshotPacket_RoundTrip() {
			var original = new PlayerSnapshotPacket {
				TickNumber = 500,
				PlayerId = 7,
				Position = new Vector3(1.5f, 20.0f, -3.7f),
				Velocity = new Vector3(0.1f, -9.8f, 0.5f),
				CameraAngle = new Vector2(45.0f, -15.0f),
				AnimationState = 2,
			};

			byte[] data = original.Serialize();
			var deserialized = (PlayerSnapshotPacket)Packet.Deserialize(data);

			Assert.Equal(original.TickNumber, deserialized.TickNumber);
			Assert.Equal(original.PlayerId, deserialized.PlayerId);
			Assert.Equal(original.Position, deserialized.Position);
			Assert.Equal(original.Velocity, deserialized.Velocity);
			Assert.Equal(original.CameraAngle, deserialized.CameraAngle);
			Assert.Equal(original.AnimationState, deserialized.AnimationState);
		}

		[Fact]
		public void WorldSnapshotPacket_RoundTrip() {
			var original = new WorldSnapshotPacket {
				TickNumber = 100,
				Players = new WorldSnapshotPacket.PlayerEntry[] {
					new() {
						PlayerId = 0,
						Position = new Vector3(10, 20, 30),
						Velocity = new Vector3(1, 0, -1),
						CameraAngle = new Vector2(90, 0),
						Health = 100f,
						AnimationState = 1,
					},
					new() {
						PlayerId = 3,
						Position = new Vector3(-5, 64, 12.5f),
						Velocity = Vector3.Zero,
						CameraAngle = new Vector2(180, -45),
						Health = 50f,
						AnimationState = 0,
					},
				},
			};

			byte[] data = original.Serialize();
			var deserialized = (WorldSnapshotPacket)Packet.Deserialize(data);

			Assert.Equal(original.TickNumber, deserialized.TickNumber);
			Assert.Equal(original.Players.Length, deserialized.Players.Length);

			for (int i = 0; i < original.Players.Length; i++) {
				Assert.Equal(original.Players[i].PlayerId, deserialized.Players[i].PlayerId);
				Assert.Equal(original.Players[i].Position, deserialized.Players[i].Position);
				Assert.Equal(original.Players[i].Velocity, deserialized.Players[i].Velocity);
				Assert.Equal(original.Players[i].CameraAngle, deserialized.Players[i].CameraAngle);
				Assert.Equal(original.Players[i].Health, deserialized.Players[i].Health);
				Assert.Equal(original.Players[i].AnimationState, deserialized.Players[i].AnimationState);
			}
		}

		[Fact]
		public void WorldSnapshotPacket_EmptyPlayers_RoundTrip() {
			var original = new WorldSnapshotPacket {
				TickNumber = 1,
				Players = Array.Empty<WorldSnapshotPacket.PlayerEntry>(),
			};

			byte[] data = original.Serialize();
			var deserialized = (WorldSnapshotPacket)Packet.Deserialize(data);

			Assert.Empty(deserialized.Players);
		}

		[Fact]
		public void InputStatePacket_RoundTrip() {
			var original = new InputStatePacket {
				TickNumber = 999,
				KeysBitmask = 0xDEADBEEF12345678UL,
				CameraAngle = new Vector2(123.4f, -56.7f),
				MouseWheel = 3.0f,
				NoClip = true,
			};

			byte[] data = original.Serialize();
			var deserialized = (InputStatePacket)Packet.Deserialize(data);

			Assert.Equal(original.TickNumber, deserialized.TickNumber);
			Assert.Equal(original.KeysBitmask, deserialized.KeysBitmask);
			Assert.Equal(original.CameraAngle, deserialized.CameraAngle);
			Assert.Equal(original.MouseWheel, deserialized.MouseWheel);
			Assert.True(deserialized.NoClip);
		}

		[Fact]
		public void Deserialize_UnknownType_Throws() {
			byte[] data = new byte[] { 0xFF, 0x00, 0x00 };
			Assert.Throws<InvalidOperationException>(() => Packet.Deserialize(data));
		}
	}

	// ──────────────────────────────────────────────
	// InputStatePacket PackKeys / UnpackKeys Tests
	// ──────────────────────────────────────────────
	public class InputStatePackKeys_Tests {
		[Fact]
		public unsafe void PackKeys_AllFalse_ProducesZeroBitmask() {
			var state = new InputState();
			var packet = new InputStatePacket();
			packet.PackKeys(state);

			Assert.Equal(0UL, packet.KeysBitmask);
		}

		[Fact]
		public unsafe void PackKeys_SingleKey_SetsCorrectBit() {
			var state = new InputState();
			state.KeysDown[(int)InputKey.W] = true;

			var packet = new InputStatePacket();
			packet.PackKeys(state);

			ulong expected = 1UL << (int)InputKey.W;
			Assert.Equal(expected, packet.KeysBitmask);
		}

		[Fact]
		public unsafe void PackUnpack_MultipleKeys_RoundTrip() {
			var state = new InputState();
			state.KeysDown[(int)InputKey.W] = true;
			state.KeysDown[(int)InputKey.Space] = true;
			state.KeysDown[(int)InputKey.Shift] = true;
			state.KeysDown[(int)InputKey.Click_Left] = true;

			var packet = new InputStatePacket();
			packet.PackKeys(state);

			var result = new InputState();
			packet.UnpackKeys(ref result);

			int keyCount = (int)InputKey.InputKeyCount;
			for (int i = 0; i < keyCount; i++) {
				Assert.Equal(state.KeysDown[i], result.KeysDown[i]);
			}
		}

		[Fact]
		public unsafe void PackUnpack_AllKeys_RoundTrip() {
			var state = new InputState();
			int keyCount = (int)InputKey.InputKeyCount;
			for (int i = 0; i < keyCount; i++)
				state.KeysDown[i] = true;

			var packet = new InputStatePacket();
			packet.PackKeys(state);

			var result = new InputState();
			packet.UnpackKeys(ref result);

			for (int i = 0; i < keyCount; i++)
				Assert.True(result.KeysDown[i], $"Key {(InputKey)i} should be true");
		}

		[Fact]
		public unsafe void PackUnpack_ThroughSerialization_RoundTrip() {
			var state = new InputState();
			state.KeysDown[(int)InputKey.A] = true;
			state.KeysDown[(int)InputKey.D] = true;
			state.KeysDown[(int)InputKey.Space] = true;

			var original = new InputStatePacket { TickNumber = 42, MouseWheel = 1.5f, CameraAngle = new Vector2(90, 0) };
			original.PackKeys(state);

			byte[] data = original.Serialize();
			var deserialized = (InputStatePacket)Packet.Deserialize(data);

			var result = new InputState();
			deserialized.UnpackKeys(ref result);

			int keyCount = (int)InputKey.InputKeyCount;
			for (int i = 0; i < keyCount; i++)
				Assert.Equal(state.KeysDown[i], result.KeysDown[i]);
		}
	}

	// ──────────────────────────────────────────────
	// ClientPrediction Tests
	// ──────────────────────────────────────────────
	public class ClientPredictionTests {
		[Fact]
		public void ProcessServerSnapshot_AccuratePrediction_ReturnsFalse() {
			var prediction = new ClientPrediction();
			var pos = new Vector3(10, 20, 30);
			var vel = new Vector3(1, 0, -1);

			prediction.RecordPrediction(100, pos, vel);

			bool needsCorrection = prediction.ProcessServerSnapshot(100, pos, vel);

			Assert.False(needsCorrection);
			Assert.Equal(0f, prediction.LastCorrectionDistance);
			Assert.Equal(0, prediction.ReconciliationCount);
		}

		[Fact]
		public void ProcessServerSnapshot_SmallPositionError_BelowThreshold_ReturnsFalse() {
			var prediction = new ClientPrediction();
			var pos = new Vector3(10, 20, 30);
			var vel = new Vector3(1, 0, -1);

			prediction.RecordPrediction(100, pos, vel);

			// Error just below the 0.1 threshold
			var serverPos = pos + new Vector3(0.05f, 0, 0);
			bool needsCorrection = prediction.ProcessServerSnapshot(100, serverPos, vel);

			Assert.False(needsCorrection);
		}

		[Fact]
		public void ProcessServerSnapshot_LargePositionError_AboveThreshold_ReturnsTrue() {
			var prediction = new ClientPrediction();
			var pos = new Vector3(10, 20, 30);
			var vel = new Vector3(1, 0, -1);

			prediction.RecordPrediction(100, pos, vel);

			var serverPos = pos + new Vector3(1.0f, 0, 0);
			bool needsCorrection = prediction.ProcessServerSnapshot(100, serverPos, vel);

			Assert.True(needsCorrection);
			Assert.Equal(1, prediction.ReconciliationCount);
			Assert.True(prediction.LastCorrectionDistance > ClientPrediction.CorrectionThreshold);
		}

		[Fact]
		public void ProcessServerSnapshot_VelocityDivergence_ReturnsTrue() {
			var prediction = new ClientPrediction();
			var pos = new Vector3(10, 20, 30);
			var vel = new Vector3(1, 0, 0);

			prediction.RecordPrediction(100, pos, vel);

			// Position matches exactly, but velocity differs significantly
			var serverVel = new Vector3(1, -2.0f, 0);
			bool needsCorrection = prediction.ProcessServerSnapshot(100, pos, serverVel);

			Assert.True(needsCorrection);
			Assert.Equal(1, prediction.ReconciliationCount);
		}

		[Fact]
		public void ProcessServerSnapshot_OldTick_Ignored() {
			var prediction = new ClientPrediction();

			prediction.RecordPrediction(100, Vector3.Zero, Vector3.Zero);
			prediction.ProcessServerSnapshot(100, Vector3.Zero, Vector3.Zero);

			// Send an older tick — should be ignored
			var farPos = new Vector3(999, 999, 999);
			bool result = prediction.ProcessServerSnapshot(99, farPos, Vector3.Zero);

			Assert.False(result);
		}

		[Fact]
		public void ProcessServerSnapshot_DuplicateTick_Ignored() {
			var prediction = new ClientPrediction();

			prediction.RecordPrediction(100, Vector3.Zero, Vector3.Zero);
			prediction.ProcessServerSnapshot(100, Vector3.Zero, Vector3.Zero);

			// Same tick again
			bool result = prediction.ProcessServerSnapshot(100, new Vector3(100, 0, 0), Vector3.Zero);

			Assert.False(result);
		}

		[Fact]
		public void ProcessServerSnapshot_MissingPrediction_ReturnsTrue() {
			var prediction = new ClientPrediction();

			// Don't record prediction for tick 100 — just ask the prediction system about it
			bool result = prediction.ProcessServerSnapshot(100, Vector3.Zero, Vector3.Zero);

			Assert.True(result);
			Assert.Equal(float.MaxValue, prediction.LastCorrectionDistance);
			Assert.Equal(1, prediction.ReconciliationCount);
		}

		[Fact]
		public void Reset_ClearsState() {
			var prediction = new ClientPrediction();
			prediction.RecordPrediction(100, Vector3.Zero, Vector3.Zero);
			prediction.ProcessServerSnapshot(100, new Vector3(5, 0, 0), Vector3.Zero);

			Assert.True(prediction.ReconciliationCount > 0);

			prediction.Reset();

			Assert.Equal(-1, prediction.LastServerTick);
			Assert.Equal(0, prediction.ReconciliationCount);
			Assert.Equal(0f, prediction.LastCorrectionDistance);
		}

		[Fact]
		public void RecordPrediction_CircularBufferWraparound() {
			var prediction = new ClientPrediction();

			// Fill more than the buffer size to verify wrap-around doesn't crash
			for (int i = 0; i < ClientPrediction.BufferSize + 10; i++) {
				prediction.RecordPrediction(i, new Vector3(i, 0, 0), Vector3.Zero);
			}

			// The latest entries should still be queryable
			int latestTick = ClientPrediction.BufferSize + 9;
			var serverPos = new Vector3(latestTick, 0, 0);
			bool result = prediction.ProcessServerSnapshot(latestTick, serverPos, Vector3.Zero);

			Assert.False(result); // Should match exactly
		}
	}

	// ──────────────────────────────────────────────
	// ClientInputBuffer Tests
	// ──────────────────────────────────────────────
	public class ClientInputBufferTests {
		[Fact]
		public unsafe void Record_StoresInput_TryGetInput_Retrieves() {
			var buffer = new ClientInputBuffer();
			var state = new InputState();
			state.KeysDown[(int)InputKey.W] = true;
			var angle = new Vector2(45, -10);

			buffer.Record(10, state, angle);

			Assert.Equal(1, buffer.Count);
			Assert.True(buffer.TryGetInput(10, out var retrieved));
			Assert.Equal(10, retrieved.TickNumber);
			Assert.Equal(angle, retrieved.CameraAngle);
			Assert.True(retrieved.State.KeysDown[(int)InputKey.W]);
		}

		[Fact]
		public void TryGetInput_MissingTick_ReturnsFalse() {
			var buffer = new ClientInputBuffer();

			Assert.False(buffer.TryGetInput(999, out _));
		}

		[Fact]
		public unsafe void Record_ReturnsInputStatePacket() {
			var buffer = new ClientInputBuffer();
			var state = new InputState();
			state.KeysDown[(int)InputKey.Space] = true;
			state.MouseWheel = 2.5f;
			var angle = new Vector2(90, 0);

			InputStatePacket packet = buffer.Record(50, state, angle);

			Assert.Equal(50, packet.TickNumber);
			Assert.Equal(angle, packet.CameraAngle);
			Assert.Equal(2.5f, packet.MouseWheel);
			// The Space key should be packed into the bitmask
			Assert.NotEqual(0UL, packet.KeysBitmask);
		}

		[Fact]
		public unsafe void GetInputsInRange_ReturnsCorrectRange() {
			var buffer = new ClientInputBuffer();
			var state = new InputState();

			for (int i = 0; i < 20; i++)
				buffer.Record(i, state, new Vector2(i, 0));

			// Get inputs for ticks 11 through 15 (afterTick=10, upToTick=15)
			List<BufferedInput> inputs = buffer.GetInputsInRange(10, 15);

			Assert.Equal(5, inputs.Count);
			Assert.Equal(11, inputs[0].TickNumber);
			Assert.Equal(15, inputs[4].TickNumber);
		}

		[Fact]
		public unsafe void GetInputsInRange_EmptyRange_ReturnsEmpty() {
			var buffer = new ClientInputBuffer();
			var state = new InputState();
			buffer.Record(10, state, Vector2.Zero);

			List<BufferedInput> inputs = buffer.GetInputsInRange(10, 10);
			Assert.Empty(inputs);
		}

		[Fact]
		public unsafe void CircularBuffer_Wraparound_OverwritesOld() {
			var buffer = new ClientInputBuffer();
			var state = new InputState();

			// Fill beyond buffer size
			for (int i = 0; i < ClientInputBuffer.BufferSize + 10; i++)
				buffer.Record(i, state, new Vector2(i, 0));

			Assert.Equal(ClientInputBuffer.BufferSize, buffer.Count);

			// Old tick (0) should be overwritten
			Assert.False(buffer.TryGetInput(0, out _));

			// Recent tick should be present
			int recentTick = ClientInputBuffer.BufferSize + 5;
			Assert.True(buffer.TryGetInput(recentTick, out var input));
			Assert.Equal(recentTick, input.TickNumber);
		}

		[Fact]
		public void Clear_ResetsBuffer() {
			var buffer = new ClientInputBuffer();
			var state = new InputState();
			buffer.Record(1, state, Vector2.Zero);
			buffer.Record(2, state, Vector2.Zero);

			Assert.Equal(2, buffer.Count);

			buffer.Clear();

			Assert.Equal(0, buffer.Count);
			Assert.False(buffer.TryGetInput(1, out _));
		}
	}

	// ──────────────────────────────────────────────
	// ReliableChannel Tests
	// ──────────────────────────────────────────────
	public class ReliableChannelTests {
		[Fact]
		public void WrapUnreliable_DoesNotTrack() {
			var channel = new ReliableChannel();
			byte[] packetData = new byte[] { 0x80, 0x01, 0x02 };

			byte[] raw = channel.WrapUnreliable(packetData);

			Assert.Equal(0, channel.PendingCount);
			Assert.Equal(ReliableChannel.HeaderSize + packetData.Length, raw.Length);
		}

		[Fact]
		public void TryWrapReliable_TracksInSendWindow() {
			var channel = new ReliableChannel();
			byte[] packetData = new byte[] { 0x01, 0x00 };

			Assert.True(channel.TryWrapReliable(packetData, 0f, out byte[] raw));

			Assert.Equal(1, channel.PendingCount);
			Assert.Equal(1u, channel.LocalSequence);
			Assert.Equal(ReliableChannel.HeaderSize + packetData.Length, raw.Length);
		}

		[Fact]
		public void ReliableWindow_BoundsAt64Packets() {
			var channel = new ReliableChannel();
			for (int index = 0; index < ReliableChannel.WindowSize; index++)
				Assert.True(channel.TryWrapReliable(new byte[] { (byte)index }, 0, out _));
			Assert.False(channel.TryWrapReliable(new byte[] { 65 }, 0, out _));
			Assert.Equal(64, channel.PendingCount);
		}

		[Fact]
		public void UnwrapUnreliable_ReturnsPayload() {
			var sender = new ReliableChannel();
			var receiver = new ReliableChannel();

			byte[] packetData = new byte[] { 0x80, 0xAA, 0xBB };
			byte[] raw = sender.WrapUnreliable(packetData);

			ReliableReceiveResult result = receiver.Unwrap(raw);

			Assert.True(result.IsValid);
			Assert.Equal(packetData, result.Payload);
		}

		[Fact]
		public void UnwrapReliable_ReturnsPayloadAndTracksSequence() {
			var sender = new ReliableChannel();
			var receiver = new ReliableChannel();

			byte[] packetData = new byte[] { 0x01, 0x05 };
			Assert.True(sender.TryWrapReliable(packetData, 0, out byte[] raw));

			ReliableReceiveResult result = receiver.Unwrap(raw);

			Assert.True(result.IsValid);
			Assert.False(result.IsDuplicate);
			Assert.Equal(packetData, result.Payload);
			Assert.Equal(1u, receiver.RemoteSequence);
		}

		[Fact]
		public void DuplicateReliable_IsSuppressedButAcknowledged() {
			var sender = new ReliableChannel();
			var receiver = new ReliableChannel();

			byte[] packetData = new byte[] { 0x01, 0x10 };
			Assert.True(sender.TryWrapReliable(packetData, 0, out byte[] raw));

			receiver.Unwrap(raw);
			ReliableReceiveResult duplicate = receiver.Unwrap(raw);

			Assert.True(duplicate.IsValid);
			Assert.True(duplicate.IsDuplicate);
			Assert.Null(duplicate.Payload);
			Assert.True(receiver.HasPendingAcknowledgement);
		}

		[Fact]
		public void AcknowledgementOnly_RemovesPending() {
			var server = new ReliableChannel();
			var client = new ReliableChannel();
			byte[] serverPacketData = new byte[] { 0x05, 0x01, 0x02 };
			Assert.True(server.TryWrapReliable(serverPacketData, 0, out byte[] rawFromServer));
			Assert.Equal(1, server.PendingCount);

			client.Unwrap(rawFromServer);
			ReliableReceiveResult acknowledgement = server.Unwrap(client.CreateAcknowledgement());
			Assert.True(acknowledgement.IsAckOnly);
			Assert.Equal(0, server.PendingCount);
		}

		[Fact]
		public void Retransmissions_BeforeTimeout_AreEmpty() {
			var channel = new ReliableChannel();
			byte[] data = new byte[] { 0x01 };
			channel.TryWrapReliable(data, 0, out _);

			ReliableRetransmissionBatch retransmissions = channel.CollectRetransmissions(0.1f, 0.2f);

			Assert.Empty(retransmissions.Packets);
			Assert.False(retransmissions.RetryLimitExceeded);
		}

		[Fact]
		public void Retransmissions_AfterAdaptiveTimeout_AreReturned() {
			var channel = new ReliableChannel();
			byte[] data = new byte[] { 0x01 };
			channel.TryWrapReliable(data, 0, out _);

			ReliableRetransmissionBatch retransmissions = channel.CollectRetransmissions(0.5f, 0.2f);

			Assert.Single(retransmissions.Packets);
			Assert.Equal(1, channel.PendingCount);
		}

		[Fact]
		public void UnwrapTooShort_IsInvalid() {
			var channel = new ReliableChannel();
			Assert.False(channel.Unwrap(new byte[] { 0x00, 0x01 }).IsValid);
		}

		[Fact]
		public void UnwrapNull_IsInvalid() {
			var channel = new ReliableChannel();
			Assert.False(channel.Unwrap(null).IsValid);
		}

		[Fact]
		public void BurstLargerThanWindow_CompletesInBatches() {
			var server = new ReliableChannel();
			var client = new ReliableChannel();
			int delivered = 0;
			while (delivered < 130)
			{
				int batch = Math.Min(ReliableChannel.WindowSize, 130 - delivered);
				for (int index = 0; index < batch; index++)
				{
					Assert.True(server.TryWrapReliable(BitConverter.GetBytes(delivered + index), 0, out byte[] raw));
					Assert.True(client.Unwrap(raw).IsValid);
				}
				server.Unwrap(client.CreateAcknowledgement());
				delivered += batch;
			}
			Assert.Equal(0, server.PendingCount);
			Assert.Equal(130u, server.LocalSequence);
		}

		[Fact]
		public void SequenceWraparound_SkipsReservedZero() {
			var sender = new ReliableChannel();
			var receiver = new ReliableChannel();
			var field = typeof(ReliableChannel).GetField(
				"localSequence",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.NotNull(field);
			field.SetValue(sender, uint.MaxValue - 1);

			Assert.True(sender.TryWrapReliable(new byte[] { 1 }, 0, out byte[] maximum));
			Assert.True(sender.TryWrapReliable(new byte[] { 2 }, 0, out byte[] wrapped));
			Assert.Equal(uint.MaxValue, BitConverter.ToUInt32(maximum, 1));
			Assert.Equal(1u, BitConverter.ToUInt32(wrapped, 1));
			Assert.False(receiver.Unwrap(maximum).IsDuplicate);
			Assert.False(receiver.Unwrap(wrapped).IsDuplicate);
		}

		[Fact]
		public void OutOfOrderPackets_AreAcceptedAndSelectivelyAcknowledged() {
			var sender = new ReliableChannel();
			var receiver = new ReliableChannel();
			Assert.True(sender.TryWrapReliable(new byte[] { 1 }, 0, out byte[] first));
			Assert.True(sender.TryWrapReliable(new byte[] { 2 }, 0, out byte[] second));
			Assert.True(sender.TryWrapReliable(new byte[] { 3 }, 0, out byte[] third));

			Assert.Equal(new byte[] { 3 }, receiver.Unwrap(third).Payload);
			Assert.Equal(new byte[] { 1 }, receiver.Unwrap(first).Payload);
			Assert.Equal(new byte[] { 2 }, receiver.Unwrap(second).Payload);
			sender.Unwrap(receiver.CreateAcknowledgement());

			Assert.Equal(0, sender.PendingCount);
		}

		[Fact]
		public void LostPacket_IsRecoveredWithExponentialRetransmissionBackoff() {
			var sender = new ReliableChannel();
			var receiver = new ReliableChannel();
			Assert.True(sender.TryWrapReliable(new byte[] { 42 }, 0, out _));

			Assert.Empty(sender.CollectRetransmissions(0.19f, 0).Packets);
			ReliableRetransmissionBatch firstRetry = sender.CollectRetransmissions(0.2f, 0);
			Assert.Single(firstRetry.Packets);
			Assert.Empty(sender.CollectRetransmissions(0.59f, 0).Packets);
			Assert.Single(sender.CollectRetransmissions(0.6f, 0).Packets);

			ReliableReceiveResult recovered = receiver.Unwrap(firstRetry.Packets[0]);
			Assert.Equal(new byte[] { 42 }, recovered.Payload);
			sender.Unwrap(receiver.CreateAcknowledgement());
			Assert.Equal(0, sender.PendingCount);
		}

		[Fact]
		public void RetryExhaustion_IsReportedAfterTwelveRetries() {
			var channel = new ReliableChannel();
			channel.TryWrapReliable(new byte[] { 1 }, 0, out _);
			for (int retry = 1; retry <= ReliableChannel.MaxRetries; retry++)
			{
				ReliableRetransmissionBatch batch = channel.CollectRetransmissions(retry, 0);
				Assert.False(batch.RetryLimitExceeded);
			}

			ReliableRetransmissionBatch failed = channel.CollectRetransmissions(
				ReliableChannel.MaxRetries + 1,
				0);
			Assert.True(failed.RetryLimitExceeded);
			Assert.Equal(1u, failed.FailedSequence);
		}

		[Fact]
		public void ControlQueue_AcceptsTrafficWhenBulkQueueIsSaturated() {
			var connection = new NetConnection(
				new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777),
				0);
			for (int index = 0; index < NetConnection.BulkQueueCapacity; index++)
			{
				Assert.True(connection.QueuePacket(
					new PingPacket { Timestamp = index },
					true,
					0,
					ReliableSendClass.Bulk));
			}
			Assert.False(connection.QueuePacket(
				new PingPacket(),
				true,
				0,
				ReliableSendClass.Bulk));
			Assert.True(connection.QueuePacket(
				new DisconnectPacket { Reason = "test" },
				true,
				0,
				ReliableSendClass.Control));
			Assert.Equal(128, connection.Diagnostics.BulkQueued);
			Assert.Equal(1, connection.Diagnostics.ControlQueued);
		}

		[Fact]
		public void BulkTraffic_LeavesReliableSlotsForControlTraffic() {
			using var transport = new UdpTransport();
			transport.Open();
			var connection = new NetConnection(
				new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 9),
				0);
			for (int index = 0; index < NetConnection.BulkQueueCapacity; index++)
			{
				Assert.True(connection.QueuePacket(
					new PingPacket { Timestamp = index },
					true,
					0,
					ReliableSendClass.Bulk));
			}

			connection.FlushOutgoing(transport, 0);
			Assert.Equal(NetConnection.BulkInFlightLimit, connection.Diagnostics.ReliableInFlight);
			Assert.True(connection.QueuePacket(
				new DisconnectPacket { Reason = "urgent" },
				true,
				0,
				ReliableSendClass.Control));
			connection.FlushOutgoing(transport, 0);

			Assert.Equal(0, connection.Diagnostics.ControlQueued);
			Assert.Equal(NetConnection.BulkInFlightLimit + 1, connection.Diagnostics.ReliableInFlight);
		}

		[Fact]
		public void OutgoingPayload_DoesNotReplaceRequiredAcknowledgementOnlyDatagram() {
			var sender = new ReliableChannel();
			var receiver = new ReliableChannel();
			Assert.True(sender.TryWrapReliable(new byte[] { 1 }, 0, out byte[] incoming));
			receiver.Unwrap(incoming);

			receiver.WrapUnreliable(new byte[] { 2 });

			Assert.True(receiver.HasPendingAcknowledgement);
			Assert.True(sender.Unwrap(receiver.CreateAcknowledgement()).IsAckOnly);
			Assert.Equal(0, sender.PendingCount);
		}

		[Fact]
		public void UdpTransport_CanCloseAndReopenBeforeDisposal() {
			using var transport = new UdpTransport();
			transport.Open();
			Assert.True(transport.IsActive);
			transport.Close();
			Assert.False(transport.IsActive);
			transport.Open();
			Assert.True(transport.IsActive);
		}
	}

	// ──────────────────────────────────────────────
	// SnapshotBuffer Interpolation Tests
	// ──────────────────────────────────────────────
	public class SnapshotBufferTests {
		private struct TestSnapshot {
			public float Value;
		}

		[Fact]
		public void Empty_Sample_ReturnsFalse() {
			var buffer = new SnapshotBuffer<TestSnapshot>();

			bool result = buffer.Sample(0.5f, out _, out _, out _);

			Assert.False(result);
			Assert.Equal(0, buffer.Count);
		}

		[Fact]
		public void SingleSnapshot_Sample_ReturnsFalse() {
			var buffer = new SnapshotBuffer<TestSnapshot>();
			buffer.Add(new TestSnapshot { Value = 10f }, time: 1.0f);

			bool result = buffer.Sample(1.0f, out var from, out _, out float t);

			Assert.False(result);
			Assert.Equal(10f, from.Value);
			Assert.Equal(0f, t);
		}

		[Fact]
		public void TwoSnapshots_Sample_InterpolatesMidpoint() {
			var buffer = new SnapshotBuffer<TestSnapshot>();
			buffer.Add(new TestSnapshot { Value = 0f }, time: 1.0f);
			buffer.Add(new TestSnapshot { Value = 10f }, time: 2.0f);

			bool result = buffer.Sample(1.5f, out var from, out var to, out float t);

			Assert.True(result);
			Assert.Equal(0f, from.Value);
			Assert.Equal(10f, to.Value);
			Assert.Equal(0.5f, t, 0.001f);
		}

		[Fact]
		public void Sample_BeforeOldestSnapshot_ClampsToOldest() {
			var buffer = new SnapshotBuffer<TestSnapshot>();
			buffer.Add(new TestSnapshot { Value = 5f }, time: 1.0f);
			buffer.Add(new TestSnapshot { Value = 15f }, time: 2.0f);

			bool result = buffer.Sample(0.5f, out var from, out _, out float t);

			Assert.True(result);
			Assert.Equal(5f, from.Value);
			Assert.Equal(0f, t);
		}

		[Fact]
		public void Sample_AfterNewestSnapshot_ClampsInterpolation() {
			var buffer = new SnapshotBuffer<TestSnapshot>();
			buffer.Add(new TestSnapshot { Value = 0f }, time: 1.0f);
			buffer.Add(new TestSnapshot { Value = 10f }, time: 2.0f);

			// Past the newest snapshot — should extrapolate but clamp t to [0,1]
			bool result = buffer.Sample(3.0f, out _, out var to, out float t);

			Assert.True(result);
			Assert.Equal(10f, to.Value);
			Assert.Equal(1.0f, t); // Clamped
		}

		[Fact]
		public void Reset_ClearsBuffer() {
			var buffer = new SnapshotBuffer<TestSnapshot>();
			buffer.Add(new TestSnapshot { Value = 1 }, 0f);
			buffer.Add(new TestSnapshot { Value = 2 }, 1f);

			Assert.Equal(2, buffer.Count);

			buffer.Reset();

			Assert.Equal(0, buffer.Count);
		}

		[Fact]
		public void BufferWraparound_OverwritesOldEntries() {
			var buffer = new SnapshotBuffer<TestSnapshot>();

			// Fill more than the buffer size
			for (int i = 0; i < SnapshotBuffer<TestSnapshot>.BufferSize + 5; i++)
				buffer.Add(new TestSnapshot { Value = i }, time: i * 0.1f);

			Assert.Equal(SnapshotBuffer<TestSnapshot>.BufferSize, buffer.Count);

			// Should still be able to interpolate recent entries
			float recentTime = (SnapshotBuffer<TestSnapshot>.BufferSize + 3) * 0.1f;
			bool result = buffer.Sample(recentTime, out _, out _, out _);
			Assert.True(result);
		}
	}
}
