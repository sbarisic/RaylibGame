using System.IO;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Client → Server (reliable). Reports a weapon fire event with aim data.
	/// </summary>
	public class WeaponFirePacket : Packet
	{
		public override PacketType Type => PacketType.WeaponFire;

		public byte WeaponType { get; set; }
		public Vector3 AimOrigin { get; set; }
		public Vector3 AimDirection { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(WeaponType);
			writer.WriteVector3(AimOrigin);
			writer.WriteVector3(AimDirection);
		}

		public override void Read(BinaryReader reader)
		{
			WeaponType = reader.ReadByte();
			AimOrigin = reader.ReadVector3();
			AimDirection = reader.ReadVector3();
		}
	}

	/// <summary>
	/// Server → Client (reliable). Broadcasts weapon fire visual effects to all clients.
	/// </summary>
	public class WeaponFireEffectPacket : Packet
	{
		public override PacketType Type => PacketType.WeaponFireEffect;

		public int PlayerId { get; set; }
		public byte WeaponType { get; set; }
		public Vector3 Origin { get; set; }
		public Vector3 Direction { get; set; }
		public Vector3 HitPosition { get; set; }
		public Vector3 HitNormal { get; set; }
		public byte HitType { get; set; }

		/// <summary>Network ID of the entity that was hit (0 = none).</summary>
		public int EntityNetworkId { get; set; }

		/// <summary>Player ID of the player that was hit (-1 = none).</summary>
		public int HitPlayerId { get; set; } = -1;

		public override void Write(BinaryWriter writer)
		{
			writer.Write(PlayerId);
			writer.Write(WeaponType);
			writer.WriteVector3(Origin);
			writer.WriteVector3(Direction);
			writer.WriteVector3(HitPosition);
			writer.WriteVector3(HitNormal);
			writer.Write(HitType);
			writer.Write(EntityNetworkId);
			writer.Write(HitPlayerId);
		}

		public override void Read(BinaryReader reader)
		{
			PlayerId = reader.ReadInt32();
			WeaponType = reader.ReadByte();
			Origin = reader.ReadVector3();
			Direction = reader.ReadVector3();
			HitPosition = reader.ReadVector3();
			HitNormal = reader.ReadVector3();
			HitType = reader.ReadByte();
			EntityNetworkId = reader.ReadInt32();
			HitPlayerId = reader.ReadInt32();
		}
	}

	/// <summary>
	/// Server → Client (reliable). Notifies a client that a player took damage.
	/// </summary>
	public class PlayerDamagePacket : Packet
	{
		public override PacketType Type => PacketType.PlayerDamage;

		public int TargetPlayerId { get; set; }
		public float DamageAmount { get; set; }
		public int SourcePlayerId { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(TargetPlayerId);
			writer.Write(DamageAmount);
			writer.Write(SourcePlayerId);
		}

		public override void Read(BinaryReader reader)
		{
			TargetPlayerId = reader.ReadInt32();
			DamageAmount = reader.ReadSingle();
			SourcePlayerId = reader.ReadInt32();
		}
	}
}
