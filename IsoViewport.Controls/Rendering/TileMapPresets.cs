namespace IsoViewport.Controls.Rendering;

public static class TileMapPresets
{
    private const int RealisticWorldMaxElevation = TileMap.MaxElevation;

    public static TileMap Flat(int rows, int cols, byte type = (byte)TileType.Grass)
    {
        var map = new TileMap(rows, cols);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                map.SetTile(row, col, type, TileMap.LandMinElevation);
            }
        }

        return map;
    }

    public static TileMap Ocean(int rows, int cols)
    {
        var map = new TileMap(rows, cols);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                map.SetTile(row, col, (byte)TileType.Sand, TileMap.DeepWaterElevation);
            }
        }

        return map;
    }

    public static TileMap Checkerboard(int rows, int cols)
    {
        var map = new TileMap(rows, cols);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var type = ((row + col) & 1) == 0 ? (byte)TileType.Grass : (byte)TileType.Sand;
                map.SetTile(row, col, type, TileMap.LandMinElevation);
            }
        }

        return map;
    }

    public static TileMap Hills(int rows, int cols)
    {
        var map = new TileMap(rows, cols);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var elevation = TileMap.LandMinElevation +
                                Math.Clamp((int)(Math.Sin(col * 0.3) * Math.Cos(row * 0.3) * 6 + 5), 0, 10);
                map.SetTile(row, col, (byte)TileType.Grass, (byte)elevation);
            }
        }

        return map;
    }

    public static TileMap Island(int rows, int cols)
    {
        var map = new TileMap(rows, cols);
        var cx = cols / 2f;
        var cy = rows / 2f;

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var dist = Math.Sqrt(Math.Pow(col - cx, 2) + Math.Pow(row - cy, 2));
                var elevation = Math.Clamp((int)(12 - dist * 0.18), 0, 12);
                var wetness = (ValueNoise(col * 0.19f + 0.41f, row * 0.19f + 0.73f) + 1f) * 0.5f;
                var type = elevation switch
                {
                    <= TileMap.ShallowWaterElevation => (byte)TileType.Sand,
                    <= 3 => (byte)TileType.Sand,
                    <= 5 => (byte)(wetness > 0.70f ? TileType.Swamp : TileType.Grass),
                    <= 8 => (byte)(wetness > 0.56f ? TileType.Forest : TileType.Grass),
                    >= 10 => (byte)(wetness > 0.88f ? TileType.Iron : TileType.Rock),
                    _ => (byte)(wetness > 0.62f ? TileType.Forest : TileType.Grass),
                };

                map.SetTile(row, col, type, (byte)elevation);
            }
        }

        return map;
    }

    public static TileMap RealisticWorld(int rows, int cols)
    {
        var map = new TileMap(rows, cols);
        var height = map.Elevation;
        var type = map.TileType;
        var colsScale = Math.Max(1f, cols - 1);
        var rowsScale = Math.Max(1f, rows - 1);

        for (var row = 0; row < rows; row++)
        {
            var v = row / rowsScale;

            for (var col = 0; col < cols; col++)
            {
                var u = col / colsScale;
                var continent = ContinentMask(u, v);
                var terrainNoise =
                    FractalNoise(u, v, 3.10f, 4, 0.52f, 2.03f, 0.11f, 0.37f) * 0.55f +
                    RidgeNoise(u, v, 7.40f, 3, 0.58f, 1.92f, 0.43f, 0.19f) * 0.30f +
                    FractalNoise(u, v, 13.20f, 2, 0.45f, 2.11f, 0.67f, 0.83f) * 0.15f;
                var lowlandBias = 0.10f * MathF.Sin((u * MathF.PI * 2.3f) + (v * 4.1f));
                var macroHeight = continent * 0.75f + terrainNoise * 0.45f + lowlandBias - 0.42f;
                var mountainBands = MountainBand(u, v);
                var finalHeight = macroHeight + mountainBands;
                var moisture =
                    FractalNoise(u, v, 4.20f, 3, 0.5f, 2.07f, 0.21f, 0.74f) * 0.65f +
                    (1f - MathF.Abs(v - 0.5f) * 1.55f) * 0.35f;
                var mineralRichness = Math.Clamp(
                    RidgeNoise(u, v, 9.10f, 3, 0.57f, 1.98f, 0.58f, 0.12f) * 0.70f +
                    ((FractalNoise(u, v, 15.80f, 2, 0.48f, 2.17f, 0.91f, 0.27f) + 1f) * 0.15f),
                    0f,
                    1f);
                var basinRichness = Math.Clamp(
                    (FractalNoise(u, v, 5.40f, 3, 0.54f, 2.02f, 0.14f, 0.61f) + 1f) * 0.5f,
                    0f,
                    1f);

                byte elevationValue;
                byte tileTypeValue;

                if (finalHeight < -0.20f)
                {
                    elevationValue = TileMap.DeepWaterElevation;
                    tileTypeValue = (byte)TileType.Sand;
                }
                else if (finalHeight < -0.05f)
                {
                    elevationValue = TileMap.ShallowWaterElevation;
                    tileTypeValue = (byte)TileType.Sand;
                }
                else
                {
                    var normalizedLandHeight = Math.Clamp((finalHeight + 0.05f) / 0.95f, 0f, 1f);
                    var elevation = TileMap.LandMinElevation +
                                    (int)MathF.Round(MathF.Pow(normalizedLandHeight, 0.88f) * (RealisticWorldMaxElevation - TileMap.LandMinElevation));
                    elevationValue = (byte)Math.Clamp(elevation, TileMap.LandMinElevation, RealisticWorldMaxElevation);
                    tileTypeValue = ChooseTerrainType(elevationValue, moisture, finalHeight, mineralRichness, basinRichness);
                }

                height[row, col] = elevationValue;
                type[row, col] = tileTypeValue;
            }
        }

        return map;
    }

    private static byte ChooseTerrainType(
        byte elevation,
        float moisture,
        float finalHeight,
        float mineralRichness,
        float basinRichness)
    {
        if (elevation <= 8 || finalHeight < 0.08f)
        {
            return (byte)TileType.Sand;
        }

        if ((moisture > 0.60f && elevation <= 26) ||
            (moisture > 0.48f && basinRichness > 0.76f && elevation <= 20))
        {
            return (byte)TileType.Swamp;
        }

        if (mineralRichness > 0.84f && elevation >= 48)
        {
            return (byte)TileType.RareMetals;
        }

        if (basinRichness > 0.84f && moisture < 0.30f && elevation <= 34)
        {
            return (byte)TileType.Oil;
        }

        if (mineralRichness > 0.76f && elevation >= 30)
        {
            return (byte)TileType.Iron;
        }

        if (elevation >= 72)
        {
            return (byte)TileType.Rock;
        }

        if (moisture > 0.54f)
        {
            return (byte)TileType.Forest;
        }

        if (elevation >= 56 && moisture < 0.34f)
        {
            return (byte)TileType.Rock;
        }

        return (byte)TileType.Grass;
    }

    private static float ContinentMask(float u, float v)
    {
        var centerDistance = MathF.Sqrt((u - 0.46f) * (u - 0.46f) * 1.20f + (v - 0.52f) * (v - 0.52f) * 2.10f);
        var eastMass = MathF.Sqrt((u - 0.74f) * (u - 0.74f) * 1.55f + (v - 0.44f) * (v - 0.44f) * 2.60f);
        var westernShelf = MathF.Sqrt((u - 0.18f) * (u - 0.18f) * 2.40f + (v - 0.58f) * (v - 0.58f) * 2.80f);
        var main = 1f - centerDistance * 1.65f;
        var east = 1f - eastMass * 1.85f;
        var west = 1f - westernShelf * 1.95f;
        var latFade = 1f - MathF.Abs(v - 0.5f) * 1.55f;
        return Math.Clamp(MathF.Max(main, MathF.Max(east, west)) * 0.88f + latFade * 0.22f, -1f, 1f);
    }

    private static float MountainBand(float u, float v)
    {
        var ridgeA = 1f - MathF.Abs((v - 0.36f) - MathF.Sin(u * 8.4f + 0.35f) * 0.06f) * 9.5f;
        var ridgeB = 1f - MathF.Abs((v - 0.64f) - MathF.Sin(u * 6.2f + 1.9f) * 0.05f) * 10.5f;
        var ridgeC = 1f - MathF.Abs((u - 0.57f) - MathF.Sin(v * 7.3f + 0.8f) * 0.05f) * 10.0f;
        return MathF.Max(0f, ridgeA) * 0.22f +
               MathF.Max(0f, ridgeB) * 0.18f +
               MathF.Max(0f, ridgeC) * 0.16f;
    }

    private static float FractalNoise(
        float u,
        float v,
        float frequency,
        int octaves,
        float persistence,
        float lacunarity,
        float seedX,
        float seedY)
    {
        var amplitude = 1f;
        var total = 0f;
        var weight = 0f;
        var currentFrequency = frequency;

        for (var octave = 0; octave < octaves; octave++)
        {
            total += ValueNoise(u * currentFrequency + seedX, v * currentFrequency + seedY) * amplitude;
            weight += amplitude;
            amplitude *= persistence;
            currentFrequency *= lacunarity;
        }

        return weight <= 0f ? 0f : total / weight;
    }

    private static float RidgeNoise(
        float u,
        float v,
        float frequency,
        int octaves,
        float persistence,
        float lacunarity,
        float seedX,
        float seedY)
    {
        var noise = FractalNoise(u, v, frequency, octaves, persistence, lacunarity, seedX, seedY);
        return 1f - MathF.Abs(noise * 2f - 1f);
    }

    private static float ValueNoise(float x, float y)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        var tx = SmoothStep(x - x0);
        var ty = SmoothStep(y - y0);

        var n00 = Hash01(x0, y0);
        var n10 = Hash01(x1, y0);
        var n01 = Hash01(x0, y1);
        var n11 = Hash01(x1, y1);
        var nx0 = Lerp(n00, n10, tx);
        var nx1 = Lerp(n01, n11, tx);
        return Lerp(nx0, nx1, ty) * 2f - 1f;
    }

    private static float SmoothStep(float value)
    {
        return value * value * (3f - 2f * value);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    private static float Hash01(int x, int y)
    {
        unchecked
        {
            uint hash = (uint)(x * 374761393);
            hash = (hash ^ (uint)(y * 668265263)) * 1274126177u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            return (hash & 0x00FFFFFFu) / 16777215f;
        }
    }
}
