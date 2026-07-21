using Voxelgine.Engine;

namespace Voxelgine.States;

internal sealed class DeferredColumnAcknowledgements
{
	private readonly Dictionary<Key, Entry> entries = new();
	private readonly Queue<Key> ready = new();

	internal int Count => entries.Count(static pair => pair.Value.State != State.Acknowledged);

	internal bool RegisterReceived(WorldColumnPacket packet)
	{
		ArgumentNullException.ThrowIfNull(packet);
		Key key = new(packet.StreamId, packet.X, packet.Z, packet.Revision);
		if (!entries.TryGetValue(key, out Entry existing))
		{
			entries.Add(key, new Entry(packet, State.Waiting));
			return true;
		}

		if (existing.State == State.Acknowledged)
		{
			entries[key] = existing with { State = State.Ready };
			ready.Enqueue(key);
		}
		return false;
	}

	internal bool MarkReady(int streamId, int x, int z, long revision)
	{
		Key key = new(streamId, x, z, revision);
		if (!entries.TryGetValue(key, out Entry entry) || entry.State != State.Waiting)
			return false;
		entries[key] = entry with { State = State.Ready };
		ready.Enqueue(key);
		return true;
	}

	internal bool Forget(int streamId, int x, int z, long revision) =>
		entries.Remove(new Key(streamId, x, z, revision));

	internal bool TryDequeueReady(out WorldColumnPacket packet)
	{
		while (ready.TryDequeue(out Key key))
		{
			if (!entries.TryGetValue(key, out Entry entry) || entry.State != State.Ready)
				continue;
			entries[key] = entry with { State = State.Acknowledged };
			packet = entry.Packet;
			return true;
		}

		packet = null;
		return false;
	}

	internal void Clear()
	{
		entries.Clear();
		ready.Clear();
	}

	private enum State
	{
		Waiting,
		Ready,
		Acknowledged,
	}

	private readonly record struct Entry(WorldColumnPacket Packet, State State);

	private readonly record struct Key(
		int StreamId,
		int X,
		int Z,
		long Revision);
}
