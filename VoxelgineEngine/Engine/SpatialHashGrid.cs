using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	// Spatial hash grid for chunk storage
	public class SpatialHashGrid<T> {
		private readonly int bucketSize;
		private readonly Dictionary<long, List<(Vector3, T)>> buckets = new();

		public SpatialHashGrid(int bucketSize = 64) {
			this.bucketSize = bucketSize;
		}

		private long Hash(Vector3 pos) {
			int x = (int)pos.X / bucketSize;
			int y = (int)pos.Y / bucketSize;
			int z = (int)pos.Z / bucketSize;
			return ((long)x & 0x1FFFFF) | (((long)y & 0x1FFFFF) << 21) | (((long)z & 0x1FFFFF) << 42);
		}

		public void Add(Vector3 pos, T value) {
			long h = Hash(pos);
			if (!buckets.TryGetValue(h, out var list)) {
				list = new List<(Vector3, T)>();
				buckets[h] = list;
			}
			list.Add((pos, value));
		}

		public bool TryGetValue(Vector3 pos, out T value) {
			long h = Hash(pos);
			if (buckets.TryGetValue(h, out var list)) {
				foreach (var (p, v) in list) {
					if (p == pos) {
						value = v;
						return true;
					}
				}
			}
			value = default;
			return false;
		}

		public bool ContainsKey(Vector3 pos) => TryGetValue(pos, out _);

		public void Clear() => buckets.Clear();

		public IEnumerable<T> Values {
			get {
				foreach (var list in buckets.Values)
					foreach (var (_, v) in list)
						yield return v;
			}
		}

		public IEnumerable<KeyValuePair<Vector3, T>> Items {
			get {
				foreach (var list in buckets.Values)
					foreach (var (p, v) in list)
						yield return new KeyValuePair<Vector3, T>(p, v);
			}
		}
	}
}
