using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Graphics;

/// <summary>Tracks presentation materials without writing unchanged growth stages.</summary>
public sealed class WorldPlantOverlay
{
	private readonly Dictionary<(int X, int Z), Dictionary<BlockCoordinate, ushort>> columns = new();
	public int Apply(WorldObjectColumnState column, Func<byte, ushort> material,
		Action<BlockCoordinate, ushort?> write, bool force = false)
	{
		var key = (column.X, column.Z);
		columns.TryGetValue(key, out var previous);
		if (!force && previous != null && previous.Count == column.Records.Count)
		{
			bool unchanged = true;
			for (int index = 0; index < column.Records.Count; index++)
			{
				WorldPlantRecord plant = column.Records[index];
				if (!previous.TryGetValue(plant.Position, out ushort value) || value != material(plant.GrowthStage))
				{ unchanged = false; break; }
			}
			if (unchanged) return 0;
		}
		Dictionary<BlockCoordinate, ushort> current = new(column.Records.Count);
		int writes = 0;
		foreach (WorldPlantRecord plant in column.Records)
		{
			ushort value = material(plant.GrowthStage);
			current.Add(plant.Position, value);
			if (force || previous == null || !previous.TryGetValue(plant.Position, out ushort old) || old != value)
			{ write(plant.Position, value); writes++; }
		}
		if (previous != null)
			foreach (BlockCoordinate position in previous.Keys)
				if (!current.ContainsKey(position)) { write(position, null); writes++; }
		columns[key] = current;
		return writes;
	}
	public void Clear() => columns.Clear();
}
