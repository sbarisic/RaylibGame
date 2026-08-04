namespace Voxelgine.WorldGeneration;

/// <summary>Fixed ordinal socket compatibility for facing prefab edges.</summary>
public static class CeramicSocketCompatibility
{
	/// <summary>
	/// Returns true only when the directions oppose each other and the socket types
	/// are equal using ordinal comparison.
	/// </summary>
	public static bool AreFacingSocketsCompatible(CeramicSocket first, CeramicSocket second)
	{
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		return CeramicGeometry.Opposite(first.Direction) == second.Direction
			&& string.Equals(first.Type, second.Type, StringComparison.Ordinal);
	}

	/// <summary>
	/// Returns true when compatible facing sockets form a connection. NoConnection
	/// faces are compatible with each other but never form a connection.
	/// </summary>
	public static bool CreatesConnection(CeramicSocket first, CeramicSocket second) =>
		AreFacingSocketsCompatible(first, second)
		&& !string.Equals(first.Type, CeramicSocket.NoConnection, StringComparison.Ordinal);
}
