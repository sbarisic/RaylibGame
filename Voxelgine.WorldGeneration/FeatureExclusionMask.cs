using System.Collections;

namespace Voxelgine.WorldGeneration;

/// <summary>Bounded raster membership, shared by hill and vegetation planning.</summary>
internal sealed class FeatureExclusionMask(int width, int length) : IEnumerable<PlanPoint>
{
    private readonly BitArray cells = new(checked(width * length));
    internal void Add(PlanPoint point)
    {
        if ((uint)point.X < width && (uint)point.Z < length) cells[point.X * length + point.Z] = true;
    }
    internal bool Contains(PlanPoint point) => (uint)point.X < width && (uint)point.Z < length && cells[point.X * length + point.Z];
    public IEnumerator<PlanPoint> GetEnumerator()
    {
        for (int x = 0; x < width; x++) for (int z = 0; z < length; z++)
            if (cells[x * length + z]) yield return new(x, z);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
