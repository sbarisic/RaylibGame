using System.Net;

namespace Voxelgine.Engine;

/// <summary>Bounded receive storage with round-robin peer scheduling.</summary>
public sealed class BoundedDatagramQueue
{
	public const int MaximumDatagrams = 4096, MaximumBytes = 8 * 1024 * 1024;
	private readonly object gate = new();
	private readonly Dictionary<IPEndPoint, Queue<byte[]>> peers = new();
	private readonly Queue<IPEndPoint> rotation = new();
	private int count, bytes;
	public long Dropped { get; private set; }
	public int Count { get { lock(gate) return count; } }
	public int Bytes { get { lock(gate) return bytes; } }
	public bool Enqueue(byte[] data, IPEndPoint peer)
	{
		lock(gate)
		{
			if (count >= MaximumDatagrams || data.Length > MaximumBytes - bytes) { Dropped++; return false; }
			if (!peers.TryGetValue(peer,out var queue)) { peers.Add(peer,queue=new()); rotation.Enqueue(peer); }
			queue.Enqueue(data); count++; bytes+=data.Length; return true;
		}
	}
	public List<(byte[] Data, IPEndPoint Sender)> Drain(int maximum = 512, int perPeer = 64)
	{
		List<(byte[],IPEndPoint)> result = new();
		lock(gate)
		{
			Dictionary<IPEndPoint,int> consumed = new();
			int skipped = 0;
			while (result.Count < maximum && rotation.Count > 0 && skipped < rotation.Count)
			{
				IPEndPoint peer = rotation.Dequeue(); Queue<byte[]> queue = peers[peer];
				if (consumed.GetValueOrDefault(peer) >= perPeer) { rotation.Enqueue(peer); skipped++; continue; }
				skipped=0; byte[] data=queue.Dequeue(); result.Add((data,peer)); count--; bytes-=data.Length;
				consumed[peer]=consumed.GetValueOrDefault(peer)+1;
				if(queue.Count==0) peers.Remove(peer); else rotation.Enqueue(peer);
			}
		}
		return result;
	}
	public void Clear() { lock(gate) { peers.Clear(); rotation.Clear(); count=bytes=0; } }
}
