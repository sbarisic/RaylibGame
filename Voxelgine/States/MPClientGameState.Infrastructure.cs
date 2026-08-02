using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private readonly Dictionary<MachineKey, InfrastructureStatePacket> _infrastructureStates = new();

	internal IReadOnlyDictionary<MachineKey, InfrastructureStatePacket> InfrastructureStates => _infrastructureStates;
	private void ClearInfrastructureStates() => _infrastructureStates.Clear();

	private void HandleInfrastructureState(InfrastructureStatePacket packet)
	{
		MachineKey key = new(new BlockCoordinate(packet.X, packet.Y, packet.Z), packet.Function);
		if (packet.State == InfrastructureMachineState.Removed)
		{
			if (_infrastructureStates.Remove(key))
			{
				_logging.Log(GameLogLevel.Info, "Infrastructure",
					$"removed coordinate={packet.X},{packet.Y},{packet.Z} function={packet.Function}");
			}
			return;
		}
		bool changed = !_infrastructureStates.TryGetValue(key, out InfrastructureStatePacket previous)
			|| previous.State != packet.State
			|| previous.RequestedEnabled != packet.RequestedEnabled
			|| previous.PowerSupply != packet.PowerSupply
			|| previous.PowerDemand != packet.PowerDemand
			|| previous.StructuralPoints != packet.StructuralPoints
			|| !string.Equals(previous.MissingRequirements, packet.MissingRequirements, StringComparison.Ordinal);
		_infrastructureStates[key] = packet;
		if (changed)
		{
			_logging.Log(GameLogLevel.Info, "Infrastructure",
				$"state coordinate={packet.X},{packet.Y},{packet.Z} function={packet.Function} requested={packet.RequestedEnabled} state={packet.State} supply={packet.PowerSupply} demand={packet.PowerDemand} structure={packet.StructuralPoints} missing={packet.MissingRequirements}");
		}
	}
}
