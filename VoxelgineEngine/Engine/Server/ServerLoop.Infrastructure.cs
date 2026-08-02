using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private void BroadcastInfrastructureState(InfrastructureMachineSnapshot snapshot)
	{
		_server.Broadcast(CreateInfrastructureStatePacket(snapshot), true, CurrentTime);
	}

	private void SendInfrastructureStateTo(int playerId)
	{
		if (_infrastructure == null)
			return;
		foreach (InfrastructureMachineSnapshot snapshot in _infrastructure.Machines.OrderBy(static value => value.Key))
			_server.SendTo(playerId, CreateInfrastructureStatePacket(snapshot), true, CurrentTime);
	}

	private static InfrastructureStatePacket CreateInfrastructureStatePacket(InfrastructureMachineSnapshot snapshot)
	{
		BlockCoordinate coordinate = snapshot.Key.FunctionCoordinate;
		return new InfrastructureStatePacket
		{
			X = coordinate.X,
			Y = coordinate.Y,
			Z = coordinate.Z,
			Function = snapshot.Key.Function,
			RequestedEnabled = snapshot.RequestedEnabled,
			State = snapshot.State,
			PowerSupply = snapshot.PowerSupply,
			PowerDemand = snapshot.PowerDemand,
			StructuralPoints = snapshot.StructuralPoints,
			MissingRequirements = snapshot.MissingRequirements,
		};
	}
}
