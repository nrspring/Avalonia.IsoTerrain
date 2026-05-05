using System.Numerics;

namespace IsoViewport.Controls.Rendering;

public sealed class TileObject
{
    public int Col { get; set; }

    public int Row { get; set; }

    public byte Type { get; set; }

    public int AnimFrame { get; set; }

    public bool Dirty { get; set; }
}

public enum ObjectType : byte
{
    Unit = 0,
    Tree = 1,
    Structure = 2,
}

public static class ObjectColours
{
    public static Vector3 GetColour(byte type)
    {
        return (ObjectType)type switch
        {
            ObjectType.Unit => new Vector3(0.95f, 0.25f, 0.25f),
            ObjectType.Tree => new Vector3(0.15f, 0.55f, 0.20f),
            ObjectType.Structure => new Vector3(0.70f, 0.65f, 0.30f),
            _ => new Vector3(0.95f, 0.25f, 0.25f),
        };
    }
}
