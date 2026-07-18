namespace DecorativeMosaic;

internal static class DecorativeMosaicSettings
{
    public const float AlphaThreshold = 0.05f;
    public const float MinimumTileSize = 2f;
    public const float MaximumTileSize = 200f;
    public const float MaximumAspect = 2.5f;
    public const float MinimumGroutFraction = 0.04f;
    public const float GroutFractionRange = 0.4f;
    public const float MaximumEdgeThreshold = 0.5f;
    public const float MinimumEdgeThreshold = 0.08f;
    public const float EdgeDisabledThreshold = 1e9f;
    public const float MinimumSizeFactor = 0.5f;
    public const float DetailRangeFactor = 3f;
    public const float EdgeAvoidFactor = 0.35f;
    public const float CentroidCutoffFactor = 3f;
    public const float FixedPointScale = 64f;
    public const float MaximumRotationJitter = 0.15f;
    public const float SizeJitterFactor = 0.15f;
    public const float ColorVariationScale = 0.3f;
    public const float BevelWidthFactor = 0.35f;
    public const float BevelShadeScale = 0.6f;
    public const float BevelTiltScale = 0.12f;
    public const float BoundsPaddingFactor = 0.7071068f;
    public const int LayingSteps = 1024;
    public const int OpenIterations = 3;
    public const int SiteSentinel = 2147483647;
    public const int MinimumGridSize = 4;
    public const int MaximumCanvasSize = 8192;
    public const int ScratchLength = 8;
    public const int ScratchSiteCount = 0;
    public const int ScratchMaskHashSum = 6;
    public const int ScratchMaskHashMix = 7;

    public static QualitySettings GetQuality(DecorativeMosaicQuality quality)
        => quality switch
        {
            DecorativeMosaicQuality.Balanced => new QualitySettings(5, 10, 512),
            DecorativeMosaicQuality.Ultra => new QualitySettings(8, 26, 1024),
            _ => new QualitySettings(6, 18, 768),
        };

    public static float GetSpacing(float tileSize)
        => Math.Clamp(tileSize, MinimumTileSize, MaximumTileSize);

    public static (int Width, int Height, float CellSize) GetGridSize(int width, int height, float spacing, in QualitySettings settings)
    {
        var longSide = Math.Max(Math.Max(width, height), 1);
        var cellSize = Math.Max(spacing / settings.CellsPerTile, longSide / (float)settings.MaximumGridResolution);
        var gridWidth = Math.Max((int)Math.Ceiling(width / cellSize) + 1, MinimumGridSize);
        var gridHeight = Math.Max((int)Math.Ceiling(height / cellSize) + 1, MinimumGridSize);
        return (gridWidth, gridHeight, cellSize);
    }

    public static float GetAspect(float aspect)
        => 1f + Math.Clamp(aspect, 0f, 1f) * (MaximumAspect - 1f);

    public static float GetEdgeThreshold(float edgeDetect)
        => edgeDetect <= 0f
            ? EdgeDisabledThreshold
            : MaximumEdgeThreshold - Math.Clamp(edgeDetect, 0f, 1f) * (MaximumEdgeThreshold - MinimumEdgeThreshold);

    public static float GetHalfBase(float spacing, float grout)
        => spacing * (1f - MinimumGroutFraction - Math.Clamp(grout, 0f, 1f) * GroutFractionRange) * 0.5f;

    public static int GetVisibleCount(float laying)
        => Math.Clamp((int)MathF.Ceiling(Math.Clamp(laying, 0f, 1f) * LayingSteps), 0, LayingSteps);

    public static int GetJumpFloodPassCount(int width, int height)
    {
        var maxSide = Math.Max(Math.Max(width, height), 1);
        var count = 0;
        var step = 1;
        while (step < maxSide)
        {
            step <<= 1;
            count++;
        }
        return Math.Max(count, 1);
    }

    internal readonly record struct QualitySettings(int CellsPerTile, int LloydIterations, int MaximumGridResolution);
}
