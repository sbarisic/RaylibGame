using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelgine.Engine {
	/// <summary>
	/// Flat spatial hash map keyed by integer Vector3 positions.
	/// Uses the complete integer coordinates as its key so distant positions cannot
	/// alias after a fixed-width bit pack wraps.
	/// Items/Values expose struct enumerators for zero-allocation foreach in hot paths.
	/// </summary>
	public class SpatialHashGrid<T> {
		readonly Dictionary<GridKey, (Vector3 Key, T Value)> _entries = new();

		static GridKey GetKey(Vector3 pos) {
			if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
				throw new ArgumentOutOfRangeException(nameof(pos), "Grid coordinates must be finite.");

			double floorX = Math.Floor(pos.X);
			double floorY = Math.Floor(pos.Y);
			double floorZ = Math.Floor(pos.Z);
			if (floorX < int.MinValue || floorX > int.MaxValue
				|| floorY < int.MinValue || floorY > int.MaxValue
				|| floorZ < int.MinValue || floorZ > int.MaxValue)
				throw new ArgumentOutOfRangeException(nameof(pos), "Grid coordinates must fit in 32-bit integers.");
			int x = (int)floorX;
			int y = (int)floorY;
			int z = (int)floorZ;
			return new GridKey(x, y, z);
		}

		public int Count => _entries.Count;

		public void Add(Vector3 pos, T value) {
			GridKey key = GetKey(pos);
			_entries[key] = (new Vector3(key.X, key.Y, key.Z), value);
		}

		public bool TryGetValue(Vector3 pos, out T value) {
			if (_entries.TryGetValue(GetKey(pos), out var entry)) {
				value = entry.Value;
				return true;
			}
			value = default;
			return false;
		}

		public bool ContainsKey(Vector3 pos) => _entries.ContainsKey(GetKey(pos));

		public bool Remove(Vector3 pos) => _entries.Remove(GetKey(pos));

		public void Clear() => _entries.Clear();

		public ValuesEnumerable Values => new(_entries);

		public ItemsEnumerable Items => new(_entries);

		public readonly struct ValuesEnumerable {
			readonly Dictionary<GridKey, (Vector3 Key, T Value)> _dict;
			internal ValuesEnumerable(Dictionary<GridKey, (Vector3 Key, T Value)> dict) => _dict = dict;
			public Enumerator GetEnumerator() => new(_dict.GetEnumerator());

			public T[] ToArray() {
				var arr = new T[_dict.Count];
				int i = 0;
				foreach (var e in _dict.Values)
					arr[i++] = e.Value;
				return arr;
			}

			public struct Enumerator {
				Dictionary<GridKey, (Vector3 Key, T Value)>.Enumerator _inner;
				internal Enumerator(Dictionary<GridKey, (Vector3 Key, T Value)>.Enumerator inner) => _inner = inner;
				public bool MoveNext() => _inner.MoveNext();
				public T Current => _inner.Current.Value.Value;
			}
		}

		public readonly struct ItemsEnumerable {
			readonly Dictionary<GridKey, (Vector3 Key, T Value)> _dict;
			internal ItemsEnumerable(Dictionary<GridKey, (Vector3 Key, T Value)> dict) => _dict = dict;
			public Enumerator GetEnumerator() => new(_dict.GetEnumerator());

			public KeyValuePair<Vector3, T>[] ToArray() {
				var arr = new KeyValuePair<Vector3, T>[_dict.Count];
				int i = 0;
				foreach (var e in _dict.Values)
					arr[i++] = new KeyValuePair<Vector3, T>(e.Key, e.Value);
				return arr;
			}

			public struct Enumerator {
				Dictionary<GridKey, (Vector3 Key, T Value)>.Enumerator _inner;
				internal Enumerator(Dictionary<GridKey, (Vector3 Key, T Value)>.Enumerator inner) => _inner = inner;
				public bool MoveNext() => _inner.MoveNext();
				public KeyValuePair<Vector3, T> Current {
					get {
						var e = _inner.Current.Value;
						return new KeyValuePair<Vector3, T>(e.Key, e.Value);
					}
				}
			}
		}

		internal readonly record struct GridKey(int X, int Y, int Z);
	}
}
