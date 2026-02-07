using System;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// A timestamped snapshot entry for <see cref="SnapshotBuffer{T}"/>.
	/// </summary>
	public struct TimestampedSnapshot<T> where T : struct
	{
		public T Data;
		public float Time;
	}

	/// <summary>
	/// Generic circular buffer of timestamped snapshots for network interpolation.
	/// Stores recent snapshots and provides interpolation between the two entries
	/// that bracket a given render time (typically current time minus interpolation delay).
	/// </summary>
	/// <remarks>
	/// Used for remote player positions/angles and entity positions.
	/// The buffer holds enough entries for ~500ms of snapshots at 66.6 Hz (~32 entries).
	/// Snapshots should be added in chronological order.
	/// </remarks>
	/// <typeparam name="T">The snapshot data type (must be a value type).</typeparam>
	public class SnapshotBuffer<T> where T : struct
	{
		/// <summary>Number of snapshot slots in the ring buffer.</summary>
		public const int BufferSize = 32;

		private readonly TimestampedSnapshot<T>[] _buffer = new TimestampedSnapshot<T>[BufferSize];
		private int _count;
		private int _head; // Next write position

		/// <summary>Number of snapshots currently stored in the buffer.</summary>
		public int Count => _count;

		/// <summary>
		/// Adds a new snapshot to the buffer with the given timestamp.
		/// Older entries are overwritten when the buffer is full.
		/// </summary>
		public void Add(T data, float time)
		{
			_buffer[_head] = new TimestampedSnapshot<T> { Data = data, Time = time };
			_head = (_head + 1) % BufferSize;
			if (_count < BufferSize)
				_count++;
		}

		/// <summary>
		/// Finds the two snapshots that bracket the given render time and computes the interpolation factor.
		/// Returns true if two valid bracketing snapshots were found.
		/// Returns false if there are fewer than 2 snapshots (outputs the single/default snapshot with t=0).
		/// </summary>
		/// <param name="renderTime">The time to interpolate at (typically current time - interpolation delay).</param>
		/// <param name="from">The snapshot just before <paramref name="renderTime"/>.</param>
		/// <param name="to">The snapshot just after <paramref name="renderTime"/>.</param>
		/// <param name="t">Interpolation factor [0..1] between <paramref name="from"/> and <paramref name="to"/>.</param>
		/// <returns>True if interpolation is possible (at least 2 snapshots); false otherwise.</returns>
		public bool Sample(float renderTime, out T from, out T to, out float t)
		{
			if (_count == 0)
			{
				from = default;
				to = default;
				t = 0f;
				return false;
			}

			if (_count == 1)
			{
				int idx = (_head - 1 + BufferSize) % BufferSize;
				from = _buffer[idx].Data;
				to = from;
				t = 0f;
				return false;
			}

			// Find the two snapshots that bracket renderTime.
			// Iterate from oldest to newest. The oldest entry is at (_head - _count + BufferSize) % BufferSize.
			int oldest = (_head - _count + BufferSize) % BufferSize;

			// If renderTime is before the oldest snapshot, clamp to oldest
			if (renderTime <= _buffer[oldest].Time)
			{
				from = _buffer[oldest].Data;
				to = from;
				t = 0f;
				return true;
			}

			// If renderTime is after the newest snapshot, extrapolate from the last two
			int newest = (_head - 1 + BufferSize) % BufferSize;
			if (renderTime >= _buffer[newest].Time)
			{
				int prevNewest = (_head - 2 + BufferSize) % BufferSize;
				from = _buffer[prevNewest].Data;
				to = _buffer[newest].Data;
				float delta = _buffer[newest].Time - _buffer[prevNewest].Time;
				if (delta > 0f)
					t = Math.Clamp((renderTime - _buffer[prevNewest].Time) / delta, 0f, 1f);
				else
					t = 1f;
				return true;
			}

			// Find the pair that brackets renderTime
			for (int i = 0; i < _count - 1; i++)
			{
				int idxA = (oldest + i) % BufferSize;
				int idxB = (oldest + i + 1) % BufferSize;

				if (renderTime >= _buffer[idxA].Time && renderTime <= _buffer[idxB].Time)
				{
					from = _buffer[idxA].Data;
					to = _buffer[idxB].Data;
					float delta = _buffer[idxB].Time - _buffer[idxA].Time;
					if (delta > 0f)
						t = (renderTime - _buffer[idxA].Time) / delta;
					else
						t = 0f;
					return true;
				}
			}

			// Fallback — shouldn't reach here, but return newest
			from = _buffer[newest].Data;
			to = from;
			t = 0f;
			return true;
		}

		/// <summary>
		/// Resets the buffer, removing all stored snapshots.
		/// </summary>
		public void Reset()
		{
			_count = 0;
			_head = 0;
		}
	}
}
