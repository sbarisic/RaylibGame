using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Engine;

public sealed class InfrastructureStatePacket : Packet
{
	public override PacketType Type => PacketType.InfrastructureState;

	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }
	public InfrastructureFunctionKind Function { get; set; }
	public bool RequestedEnabled { get; set; }
	public InfrastructureMachineState State { get; set; }
	public int PowerSupply { get; set; }
	public int PowerDemand { get; set; }
	public int StructuralPoints { get; set; }
	public string MissingRequirements { get; set; } = string.Empty;

	public override void Write(BinaryWriter writer)
	{
		writer.Write(X);
		writer.Write(Y);
		writer.Write(Z);
		writer.Write((byte)Function);
		writer.Write(RequestedEnabled);
		writer.Write((byte)State);
		writer.Write(PowerSupply);
		writer.Write(PowerDemand);
		writer.Write(StructuralPoints);
		writer.Write(MissingRequirements ?? string.Empty);
	}

	public override void Read(BinaryReader reader)
	{
		X = reader.ReadInt32();
		Y = reader.ReadInt32();
		Z = reader.ReadInt32();
		Function = (InfrastructureFunctionKind)reader.ReadByte();
		RequestedEnabled = reader.ReadBoolean();
		State = (InfrastructureMachineState)reader.ReadByte();
		PowerSupply = reader.ReadInt32();
		PowerDemand = reader.ReadInt32();
		StructuralPoints = reader.ReadInt32();
		MissingRequirements = reader.ReadString();
		if (!Enum.IsDefined(Function) || !Enum.IsDefined(State))
			throw new InvalidDataException("Infrastructure state packet contains an unknown enum value.");
	}
}
