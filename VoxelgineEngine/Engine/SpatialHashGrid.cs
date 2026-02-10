using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelgine.Engine {
	/// <summary>
	/// Flat spatial hash map keyed by Vector3 position.
	/// Uses packed long keys for O(1) lookup without per-entry List overhead.
	/// Items/Values expose struct enumerators for zero-allocation foreach in hot paths.
	/// </summary>
	public class SpatialHashGrid<T> {
		readonly Dictionary<long, (Vector3 Key, T Value)> _entries = new();

		static long Pack(Vector3 pos) {
			int x = (int)MathF.Floor(pos.X);
			int y = (int)MathF.Floor(pos.Y);
			int z = (int)MathF.Floor(pos.Z);
			return (x & 0x1FFFFFL) | ((y & 0x1FFFFFL) << 21) | ((z & 0x1FFFFFL) << 42);
		}

		public int Count => _entries.Count;

		public void Add(Vector3 pos, T value) => _entries[Pack(pos)] = (pos, value);

		public bool TryGetValue(Vector3 pos, out T value) {
			if (_entries.TryGetValue(Pack(pos), out var entry)) {
				value = entry.Value;
				return true;
			}
			value = default;
			return false;
		}

		public bool ContainsKey(Vector3 pos) => _entries.ContainsKey(Pack(pos));

		public void Clear() => _entries.Clear();

		public ValuesEnumerable Values => new(_entries);

		public ItemsEnumerable Items => new(_entries);

		public readonly struct ValuesEnumerable {
			readonly Dictionary<long, (Vector3 Key, T Value)> _dict;
			internal ValuesEnumerable(Dictionary<long, (Vector3 Key, T Value)> dict) => _dict = dict;
			public Enumerator GetEnumerator() => new(_dict.GetEnumerator());

			public T[] ToArray() {
				var arr = new T[_dict.Count];
				int i = 0;
				foreach (var e in _dict.Values)
					arr[i++] = e.Value;
				return arr;
			}

			public struct Enumerator {
				Dictionary<long, (Vector3 Key, T Value)>.Enumerator _inner;
				internal Enumerator(Dictionary<long, (Vector3 Key, T Value)>.Enumerator inner) => _inner = inner;
				public bool MoveNext() => _inner.MoveNext();
				public T Current => _inner.Current.Value.Value;
			}
		}

		public readonly struct ItemsEnumerable {
			readonly Dictionary<long, (Vector3 Key, T Value)> _dict;
			internal ItemsEnumerable(Dictionary<long, (Vector3 Key, T Value)> dict) => _dict = dict;
			public Enumerator GetEnumerator() => new(_dict.GetEnumerator());

			public KeyValuePair<Vector3, T>[] ToArray() {
				var arr = new KeyValuePair<Vector3, T>[_dict.Count];
				int i = 0;
				foreach (var e in _dict.Values)
					arr[i++] = new KeyValuePair<Vector3, T>(e.Key, e.Value);
				return arr;
			}

			public struct Enumerator {
				Dictionary<long, (Vector3 Key, T Value)>.Enumerator _inner;
				internal Enumerator(Dictionary<long, (Vector3 Key, T Value)>.Enumerator inner) => _inner = inner;
				public bool MoveNext() => _inner.MoveNext();
				public KeyValuePair<Vector3, T> Current {
					get {
						var e = _inner.Current.Value;
						return new KeyValuePair<Vector3, T>(e.Key, e.Value);
					}
				}
			}
		}
	}
}
