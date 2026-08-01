using System;
using System.Numerics;

namespace Voxelgine.Engine;

public readonly record struct SimulatedCommandRecord(
	int Tick,
	byte SelectedHotbarSlot,
	Vector3 InteractionOrigin,
	Vector3 InteractionDirection,
	bool PrimaryUse,
	bool SecondaryUse);

public sealed class SimulatedCommandHistory
{
	public const int Capacity = 128;

	private readonly SimulatedCommandRecord[] _records = new SimulatedCommandRecord[Capacity];
	private readonly bool[] _occupied = new bool[Capacity];

	public int LatestTick { get; private set; }
	public int OldestTick => LatestTick <= 0 ? 0 : Math.Max(1, LatestTick - Capacity + 1);

	public void Record(in SimulatedCommandRecord record)
	{
		if (record.Tick <= 0)
			throw new ArgumentOutOfRangeException(nameof(record));
		int index = record.Tick % Capacity;
		_records[index] = record;
		_occupied[index] = true;
		LatestTick = Math.Max(LatestTick, record.Tick);
	}

	public bool TryGet(int tick, out SimulatedCommandRecord record)
	{
		if (tick <= 0)
		{
			record = default;
			return false;
		}
		int index = tick % Capacity;
		record = _records[index];
		return _occupied[index] && record.Tick == tick;
	}

	public void Clear()
	{
		Array.Clear(_records);
		Array.Clear(_occupied);
		LatestTick = 0;
	}
}
