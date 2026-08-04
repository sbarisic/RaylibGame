using System;
using System.Collections.Generic;
using System.Text;

namespace Voxelgine.WorldGeneration
{
    // Base structure, you control the whole world generator from here
    public interface ICeramicFish
    {
        // TODO: Need function ideas what we might need here
    }

    public enum CeramicRotation
    {
        RotNone,

        Rot0,
        Rot90CW,
        Rot180CW,
        Rot270CW,

        RotAll = Rot0 | Rot90CW | Rot180CW | Rot270CW
    }

    public enum CeramicFlip
    {
        FlipNone,

        FlipHorizontal,
        FlipVertical,

        FlipAll = FlipHorizontal | FlipVertical
    }

    public interface ICeramicPrefab
    {
        public int SizeX { get; set; }

        public int SizeY { get; set; }

        // 2D array containing the raw value "entities" for the prefab
        public int[] Value { get; set; }

        public CeramicRotation AllowedRotations { get; set; }

        public CeramicFlip AllowedFlips { get; set; }

        // If multiple prefabs fit in, choose one with lowest priority. Multiple prefabs at the same priority get choosen at random
        public int Priority { get; set; }

        // Socket "shape" this prefab supplies
        // Socket definitions are raw string ID-s
        public string SocketUp { get; set; }
        public string SocketRight { get; set; }
        public string SocketDown { get; set; }
        public string SocketLeft { get; set; }

        // All socket "shapes" this prefab accepts at appropriate parts
        public string[] SocketAcceptUp { get; set; }
        public string[] SocketAcceptRight { get; set; }
        public string[] SocketAcceptDown { get; set; }
        public string[] SocketAcceptLeft { get; set; }
    }
}
