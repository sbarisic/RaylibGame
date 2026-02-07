using System;
using System.Collections.Generic;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Combines multiple wrapped network packets into batched UDP datagrams (up to MTU)
	/// and splits received batched datagrams back into individual packets.
	/// </summary>
	/// <remarks>
	/// <para>Single-packet datagrams are sent without batching overhead.</para>
	/// <para>
	/// Batch wire format: <c>[0xFF][count:1][len1:2][pkt1...][len2:2][pkt2...]...</c>
	/// </para>
	/// <para>
	/// The 0xFF marker byte distinguishes batched datagrams from single packets,
	/// which start with 0x00 or 0x01 (the <see cref="ReliableChannel"/> reliable flag).
	/// </para>
	/// </remarks>
	public static class PacketBatcher
	{
		/// <summary>
		/// Marker byte identifying a batched datagram. Cannot conflict with
		/// the reliable flag byte (0 = unreliable, 1 = reliable).
		/// </summary>
		public const byte BatchMarker = 0xFF;

		/// <summary>
		/// Default maximum transmission unit in bytes.
		/// Datagrams will not exceed this size when batching.
		/// </summary>
		public const int DefaultMtu = 1200;

		/// <summary>
		/// Batch header size: marker (1) + count (1).
		/// </summary>
		private const int BatchHeaderSize = 2;

		/// <summary>
		/// Per-packet overhead within a batch: length prefix (2 bytes, little-endian ushort).
		/// </summary>
		private const int EntryOverhead = 2;

		/// <summary>
		/// Groups wrapped packets into one or more batched datagrams, each up to
		/// <paramref name="mtu"/> bytes. A single-packet batch is sent without
		/// batch framing overhead. Packets that individually exceed MTU (minus batch
		/// framing) are sent standalone.
		/// </summary>
		/// <param name="rawPackets">List of raw wrapped packets to batch.</param>
		/// <param name="mtu">Maximum datagram size in bytes.</param>
		/// <returns>List of datagrams ready for UDP transmission.</returns>
		public static List<byte[]> CreateBatchedDatagrams(List<byte[]> rawPackets, int mtu = DefaultMtu)
		{
			var result = new List<byte[]>();

			if (rawPackets.Count == 0)
				return result;

			if (rawPackets.Count == 1)
			{
				result.Add(rawPackets[0]);
				return result;
			}

			var currentBatch = new List<byte[]>();
			int currentSize = BatchHeaderSize;

			foreach (var pkt in rawPackets)
			{
				int entrySize = EntryOverhead + pkt.Length;

				if (currentSize + entrySize > mtu && currentBatch.Count > 0)
				{
					FlushBatch(currentBatch, result);
					currentBatch.Clear();
					currentSize = BatchHeaderSize;
				}

				// Oversized single packet: send standalone without batch framing
				if (currentBatch.Count == 0 && BatchHeaderSize + entrySize > mtu)
				{
					result.Add(pkt);
					continue;
				}

				currentBatch.Add(pkt);
				currentSize += entrySize;
			}

			if (currentBatch.Count > 0)
				FlushBatch(currentBatch, result);

			return result;
		}

		/// <summary>
		/// Splits a received datagram into individual wrapped packets.
		/// If the first byte is not <see cref="BatchMarker"/>, the datagram is
		/// returned as a single-element list (standard non-batched packet).
		/// </summary>
		/// <param name="data">Raw datagram bytes received from UDP.</param>
		/// <returns>List of individual wrapped packets.</returns>
		public static List<byte[]> UnbatchDatagram(byte[] data)
		{
			var result = new List<byte[]>();

			if (data == null || data.Length == 0)
				return result;

			if (data[0] != BatchMarker)
			{
				result.Add(data);
				return result;
			}

			if (data.Length < BatchHeaderSize)
				return result;

			int count = data[1];
			int offset = BatchHeaderSize;

			for (int i = 0; i < count && offset + EntryOverhead <= data.Length; i++)
			{
				ushort len = (ushort)(data[offset] | (data[offset + 1] << 8));
				offset += EntryOverhead;

				if (offset + len > data.Length)
					break;

				byte[] pkt = new byte[len];
				Buffer.BlockCopy(data, offset, pkt, 0, len);
				result.Add(pkt);
				offset += len;
			}

			return result;
		}

		private static void FlushBatch(List<byte[]> packets, List<byte[]> result)
		{
			if (packets.Count == 1)
			{
				result.Add(packets[0]);
				return;
			}

			int totalSize = BatchHeaderSize;
			foreach (var pkt in packets)
				totalSize += EntryOverhead + pkt.Length;

			byte[] batch = new byte[totalSize];
			batch[0] = BatchMarker;
			batch[1] = (byte)Math.Min(packets.Count, 255);

			int offset = BatchHeaderSize;
			foreach (var pkt in packets)
			{
				batch[offset] = (byte)(pkt.Length & 0xFF);
				batch[offset + 1] = (byte)((pkt.Length >> 8) & 0xFF);
				offset += EntryOverhead;
				Buffer.BlockCopy(pkt, 0, batch, offset, pkt.Length);
				offset += pkt.Length;
			}

			result.Add(batch);
		}
	}
}
