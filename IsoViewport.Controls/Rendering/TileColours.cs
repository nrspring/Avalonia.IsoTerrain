using System.Numerics;

namespace IsoViewport.Controls.Rendering;

public enum TileType : byte
{
    Grass = 0,
    Sand = 1,
    Swamp = 2,
    Forest = 3,
    Stone = 4,
    Iron = 5,
    Oil = 6,
    RareMetals = 7,
    Water = 8,
}

public static class TileColours
{
    private const float DetailedVisualMaxElevation = TileMap.LandMinElevation + 20f;
    private const float LandElevationShadePerStep = 0.1f;
    private static readonly Vector3 DeepWaterColour = new(0.08f, 0.24f, 0.45f);
    private static readonly Vector3 ShallowWaterColour = new(0.30f, 0.64f, 0.84f);
    private static readonly Vector3 HeatDeepWaterColour = new(0.04f, 0.13f, 0.42f);
    private static readonly Vector3 HeatShallowWaterColour = new(0.06f, 0.48f, 0.78f);
    private static readonly Vector3 HeatLowlandColour = new(0.08f, 0.60f, 0.22f);
    private static readonly Vector3 HeatHillColour = new(0.86f, 0.78f, 0.18f);
    private static readonly Vector3 HeatHighlandColour = new(0.88f, 0.32f, 0.12f);
    private static readonly Vector3 HeatPeakColour = new(0.96f, 0.96f, 0.90f);

    public static Vector3 GetTopBorderColour(Vector3 top, bool isWater = false)
    {
        return isWater
            ? top * new Vector3(0.78f, 0.78f, 0.78f)
            : top * new Vector3(0.62f, 0.62f, 0.62f);
    }

    public static (Vector3 top, Vector3 left, Vector3 right) GetFaceColours(byte tileType, byte elev)
    {
        var isWater = TileMap.IsWaterTile(tileType, elev);
        var top = isWater
            ? elev <= TileMap.DeepWaterElevation ? DeepWaterColour : ShallowWaterColour
            : (TileType)tileType switch
        {
            TileType.Grass => new Vector3(0.30f, 0.62f, 0.25f),
            TileType.Sand => new Vector3(0.85f, 0.78f, 0.50f),
            TileType.Swamp => new Vector3(0.18f, 0.42f, 0.33f),
            TileType.Forest => new Vector3(0.06f, 0.32f, 0.20f),
            TileType.Stone => new Vector3(0.50f, 0.48f, 0.45f),
            TileType.Iron => new Vector3(0.62f, 0.36f, 0.24f),
            TileType.Oil => new Vector3(0.12f, 0.12f, 0.14f),
            TileType.RareMetals => new Vector3(0.38f, 0.60f, 0.66f),
            _ => new Vector3(0.30f, 0.62f, 0.25f),
        };

        if (!isWater)
        {
            var elevationAboveLowland = Math.Max(0, elev - TileMap.LandMinElevation);
            var elevationShade = Math.Max(0.70f, 1f - elevationAboveLowland * LandElevationShadePerStep);
            top *= elevationShade;
        }

        return (top, top * 0.62f, top * 0.78f);
    }

    public static (Vector3 top, Vector3 left, Vector3 right) GetFaceColours(
        byte tileType,
        byte elev,
        TerrainRenderMode renderMode,
        int row,
        int col)
    {
        return renderMode switch
        {
            TerrainRenderMode.Heat => GetHeatColours(elev),
            TerrainRenderMode.Topographical => GetTopographicalColours(elev, row, col),
            TerrainRenderMode.Voxel => GetTerrainColours(tileType, elev, row, col),
            _ => GetTerrainColours(tileType, elev, row, col),
        };
    }

    private static (Vector3 top, Vector3 left, Vector3 right) GetTerrainColours(byte tileType, byte elev, int row, int col)
    {
        var colours = GetFaceColours(tileType, elev);

        if (TileMap.IsWaterTile(tileType, elev))
        {
            return colours;
        }

        var tonalNoise = (Hash01(row, col, tileType, 0x51A3) - 0.5f) * 0.10f;
        var warmNoise = (Hash01(row, col, tileType, 0x2F7D) - 0.5f) * 0.05f;
        var coolNoise = (Hash01(row, col, tileType, 0x8C31) - 0.5f) * 0.04f;
        var top = Clamp01(
            colours.top * (1f + tonalNoise) +
            new Vector3(warmNoise * 0.55f, warmNoise * 0.30f, coolNoise * 0.45f));

        return (top, top * 0.62f, top * 0.78f);
    }

    private static (Vector3 top, Vector3 left, Vector3 right) GetHeatColours(byte elev)
    {
        var heat = NormalizeDetailedElevation(elev);
        var top = elev switch
        {
            <= TileMap.DeepWaterElevation => HeatDeepWaterColour,
            <= TileMap.ShallowWaterElevation => HeatShallowWaterColour,
            _ => GetHeatRampColour(heat),
        };

        return (top, top * 0.62f, top * 0.78f);
    }

    private static (Vector3 top, Vector3 left, Vector3 right) GetTopographicalColours(byte elev, int row, int col)
    {
        var normalized = NormalizeDetailedElevation(elev);
        var paperTint = 0.975f - normalized * 0.085f;
        var top = new Vector3(paperTint, paperTint, paperTint * (1f - normalized * 0.02f));
        var left = top * 0.93f;
        var right = top * 0.97f;
        return (top, left, right);
    }

    private static float NormalizeDetailedElevation(byte elev)
    {
        return Math.Clamp(elev / DetailedVisualMaxElevation, 0f, 1f);
    }

    private static Vector3 GetHeatRampColour(float heat)
    {
        if (heat < 0.35f)
        {
            return Lerp(HeatLowlandColour, HeatHillColour, heat / 0.35f);
        }

        if (heat < 0.70f)
        {
            return Lerp(HeatHillColour, HeatHighlandColour, (heat - 0.35f) / 0.35f);
        }

        return Lerp(HeatHighlandColour, HeatPeakColour, (heat - 0.70f) / 0.30f);
    }

    private static Vector3 Lerp(Vector3 from, Vector3 to, float amount)
    {
        return from + ((to - from) * Math.Clamp(amount, 0f, 1f));
    }

    private static float Hash01(int row, int col, int seedA, int seedB)
    {
        unchecked
        {
            uint hash = (uint)(row * 374761393);
            hash = (hash ^ (uint)(col * 668265263)) * 1274126177u;
            hash ^= (uint)(seedA * 224682251);
            hash ^= (uint)(seedB * 326648991);
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            var masked = hash & 0x00FFFFFFu;
            return masked / 16777215f;
        }
    }

    private static Vector3 Clamp01(Vector3 colour)
    {
        return Vector3.Clamp(colour, Vector3.Zero, Vector3.One);
    }
}
