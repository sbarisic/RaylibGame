using System;
using System.Collections.Generic;

namespace Voxelgine.Engine;

/// <summary>
/// Bounded ordered command queue used by the authoritative server. It accepts
/// redundant, out-of-order UDP command batches and exposes only a contiguous stream.
/// </summary>
public sealed class ServerCommandQueue
{
	public const int MaximumAhead = 128;
	public const int MaximumQueued = 256;
	public const int MissingCommandWaitFrames = 2;

	private readonly SortedDictionary<int, InputCommand> _pending = new();
	private InputCommand _lastCommand;
	private int _missingFrames;

	public int LastSimulatedCommandTick { get; private set; }
	public int Count => _pending.Count;

	public int Enqueue(InputStatePacket packet)
	{
		if (packet == null)
			throw new ArgumentNullException(nameof(packet));

		int accepted = 0;
		foreach (InputCommand command in packet.Commands)
		{
			if (command.TickNumber <= LastSimulatedCommandTick ||
				command.TickNumber > LastSimulatedCommandTick + MaximumAhead ||
				!IsFinite(command))
			{
				continue;
			}

			if (_pending.ContainsKey(command.TickNumber))
				continue;

			if (_pending.Count >= MaximumQueued)
				break;

			_pending.Add(command.TickNumber, command);
			accepted++;
		}

		return accepted;
	}

	/// <summary>Advances the missing-command wait once per authoritative server frame.</summary>
	public void BeginFrame()
	{
		int expected = LastSimulatedCommandTick + 1;
		if (_pending.ContainsKey(expected) || _pending.Count == 0)
		{
			_missingFrames = 0;
			return;
		}

		if (GetOldestPendingTick() > expected)
			_missingFrames++;
	}

	public bool TryDequeue(out InputCommand command)
	{
		int expected = LastSimulatedCommandTick + 1;
		if (_pending.Remove(expected, out command))
		{
			Accept(command);
			return true;
		}

		if (_pending.Count == 0 || GetOldestPendingTick() <= expected || _missingFrames < MissingCommandWaitFrames)
		{
			command = default;
			return false;
		}

		command = _lastCommand;
		command.TickNumber = expected;
		command.MouseWheel = 0f;
		_missingFrames = 0;
		Accept(command);
		return true;
	}

	public void Clear()
	{
		_pending.Clear();
		_lastCommand = default;
		_missingFrames = 0;
		LastSimulatedCommandTick = 0;
	}

	private void Accept(InputCommand command)
	{
		_lastCommand = command;
		LastSimulatedCommandTick = command.TickNumber;
		_missingFrames = 0;
	}

	private int GetOldestPendingTick()
	{
		using IEnumerator<int> enumerator = _pending.Keys.GetEnumerator();
		return enumerator.MoveNext() ? enumerator.Current : int.MaxValue;
	}

	private static bool IsFinite(in InputCommand command) =>
		float.IsFinite(command.CameraAngle.X) &&
		float.IsFinite(command.CameraAngle.Y) &&
		float.IsFinite(command.MouseWheel);
}
