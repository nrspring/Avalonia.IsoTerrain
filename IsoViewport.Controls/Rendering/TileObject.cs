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
    StoneDeposit = 3,
    IronDeposit = 4,
    OilSeep = 5,
    RareMetalsDeposit = 6,
    SwampReeds = 7,
}

public static class ObjectColours
{
    public static Vector3 GetColour(byte type)
    {
        return (ObjectType)type switch
        {
            ObjectType.Unit => new Vector3(0.95f, 0.25f, 0.25f),
            ObjectType.Tree => new Vector3(0.03f, 0.44f, 0.26f),
            ObjectType.Structure => new Vector3(0.70f, 0.65f, 0.30f),
            ObjectType.StoneDeposit => new Vector3(0.52f, 0.51f, 0.49f),
            ObjectType.IronDeposit => new Vector3(0.70f, 0.31f, 0.18f),
            ObjectType.OilSeep => new Vector3(0.04f, 0.04f, 0.05f),
            ObjectType.RareMetalsDeposit => new Vector3(0.36f, 0.78f, 0.86f),
            ObjectType.SwampReeds => new Vector3(0.70f, 0.72f, 0.34f),
            _ => new Vector3(0.95f, 0.25f, 0.25f),
        };
    }
}
