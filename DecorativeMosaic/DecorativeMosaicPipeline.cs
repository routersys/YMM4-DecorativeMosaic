using System.Runtime.InteropServices;
using ComputeSharp;

namespace DecorativeMosaic;

internal sealed class DecorativeMosaicPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private readonly float[] _boundsMinX;
    private readonly float[] _boundsMinY;
    private readonly float[] _boundsMaxX;
    private readonly float[] _boundsMaxY;
    private ReadWriteBuffer<int>? _mask;
    private ReadWriteBuffer<float>? _luminance;
    private ReadWriteBuffer<float>? _theta;
    private ReadWriteBuffer<float>? _edgeDistance;
    private ReadWriteBuffer<int>? _jumpFloodA;
    private ReadWriteBuffer<int>? _jumpFloodB;
    private ReadWriteBuffer<int>? _siteMap;
    private ReadWriteBuffer<float>? _sitePosX;
    private ReadWriteBuffer<float>? _sitePosY;
    private ReadWriteBuffer<float>? _siteTheta;
    private ReadWriteBuffer<float>? _siteSize;
    private ReadWriteBuffer<int>? _siteRank;
    private ReadWriteBuffer<int>? _sumX;
    private ReadWriteBuffer<int>? _sumY;
    private ReadWriteBuffer<int>? _count;
    private ReadBackBuffer<float>? _posXReadBack;
    private ReadBackBuffer<float>? _posYReadBack;
    private ReadBackBuffer<int>? _rankReadBack;
    private float[]? _cachedPosX;
    private float[]? _cachedPosY;
    private int[]? _cachedRank;
    private int _cachedSiteCount;
    private StructureKey? _structureKey;
    private int _gridWidth;
    private int _gridHeight;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private DecorativeMosaicPipeline(GraphicsDevice device)
    {
        _device = device;
        _scratch = device.AllocateReadWriteBuffer<int>(DecorativeMosaicSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(DecorativeMosaicSettings.ScratchLength);
        _boundsMinX = new float[DecorativeMosaicSettings.LayingSteps];
        _boundsMinY = new float[DecorativeMosaicSettings.LayingSteps];
        _boundsMaxX = new float[DecorativeMosaicSettings.LayingSteps];
        _boundsMaxY = new float[DecorativeMosaicSettings.LayingSteps];
    }

    public static DecorativeMosaicPipeline? TryCreate()
    {
        try
        {
            return new DecorativeMosaicPipeline(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static DecorativeMosaicPipeline? TryCreate(GraphicsDevice device)
    {
        try
        {
            return new DecorativeMosaicPipeline(device);
        }
        catch
        {
            return null;
        }
    }

    internal void WaitForCompletion()
    {
        _device.For(1, new FillIntShader(_scratch, 0, 0));
    }

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsureGridFor(width, height, in parameters);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        using (ComputeContext context = _device.CreateComputeContext())
            RecordFullPipeline(in context, sourceTexture, outputTexture, width, height, in parameters);
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, in parameters);
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
        context.Submit();
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, in parameters);
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
    }

    internal bool Simulate(
        ReadWriteTexture2D<Bgra32, Float4> source,
        int canvasWidth,
        int canvasHeight,
        in Parameters parameters)
    {
        EnsureGridFor(canvasWidth, canvasHeight, in parameters);
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        using (ComputeContext context = _device.CreateComputeContext())
        {
            RecordAnalyzeStage(in context, source, canvasWidth, canvasHeight, in derived);
            RecordMaskHashStage(in context);
        }
        _scratchReadBack.CopyFrom(_scratch);
        var hashed = _scratchReadBack.Span;
        var key = new StructureKey(
            hashed[6],
            hashed[7],
            canvasWidth,
            canvasHeight,
            parameters.Quality,
            parameters.Seed,
            parameters.TileSize,
            parameters.Aspect,
            parameters.Align,
            parameters.EdgeDetect,
            parameters.Detail);
        if (_structureKey == key)
            return false;

        using (ComputeContext context = _device.CreateComputeContext())
            RecordGrowthStage(in context, in derived, in parameters);
        _scratchReadBack.CopyFrom(_scratch);
        var posXReadBack = _posXReadBack!;
        var posYReadBack = _posYReadBack!;
        var rankReadBack = _rankReadBack!;
        posXReadBack.CopyFrom(_sitePosX!);
        posYReadBack.CopyFrom(_sitePosY!);
        rankReadBack.CopyFrom(_siteRank!);
        _cachedSiteCount = _scratchReadBack.Span[0];
        posXReadBack.Span.CopyTo(_cachedPosX!);
        posYReadBack.Span.CopyTo(_cachedPosY!);
        rankReadBack.Span.CopyTo(_cachedRank!);
        BuildBoundsPrefix();
        _structureKey = key;
        return true;
    }

    internal bool TryGetVisibleBounds(int canvasWidth, int canvasHeight, in Parameters parameters, out PixelRect rect)
    {
        rect = default;
        if (_cachedSiteCount <= 0)
            return false;
        var visibleCount = DecorativeMosaicSettings.GetVisibleCount(parameters.Laying);
        if (visibleCount <= 0)
            return false;

        var index = visibleCount - 1;
        if (_boundsMinX[index] == float.MaxValue)
            return false;

        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        var padding = (int)MathF.Ceiling(derived.Gamma * derived.Spacing * DecorativeMosaicSettings.BoundsPaddingFactor) + 4;
        var left = Math.Clamp(((int)_boundsMinX[index] - padding) & ~3, 0, canvasWidth);
        var top = Math.Clamp(((int)_boundsMinY[index] - padding) & ~3, 0, canvasHeight);
        var right = Math.Clamp((int)MathF.Ceiling(_boundsMaxX[index]) + padding, 0, canvasWidth);
        var bottom = Math.Clamp((int)MathF.Ceiling(_boundsMaxY[index]) + padding, 0, canvasHeight);
        var width = Math.Min((right - left + 3) & ~3, canvasWidth - left);
        var height = Math.Min((bottom - top + 3) & ~3, canvasHeight - top);
        if (width <= 0 || height <= 0)
            return false;

        rect = new PixelRect(left, top, width, height);
        return true;
    }

    internal void RenderVisible(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int canvasWidth,
        int canvasHeight,
        PixelRect rect,
        in Parameters parameters)
    {
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        using ComputeContext context = _device.CreateComputeContext();
        RecordRenderStage(in context, source, output, rect, canvasWidth, canvasHeight, in derived, in parameters);
    }

    private void RecordFullPipeline(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        _structureKey = null;
        var derived = Derive(width, height, in parameters);
        RecordAnalyzeStage(in context, source, width, height, in derived);
        RecordGrowthStage(in context, in derived, in parameters);
        RecordRenderStage(in context, source, output, new PixelRect(0, 0, width, height), width, height, in derived, in parameters);
    }

    private void RecordAnalyzeStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        int canvasWidth,
        int canvasHeight,
        in DerivedValues derived)
    {
        context.For(_gridWidth, _gridHeight, new AnalyzeShader(
            source, _mask!, _luminance!, canvasWidth, canvasHeight, _gridWidth, _gridHeight, derived.CellSize, DecorativeMosaicSettings.AlphaThreshold));
        context.Barrier(_mask!);
        context.Barrier(_luminance!);
    }

    private void RecordMaskHashStage(in ComputeContext context)
    {
        context.For(1, new MaskHashResetShader(_scratch));
        context.Barrier(_scratch);
        context.For(_gridWidth, _gridHeight, new MaskHashShader(_mask!, _luminance!, _scratch, _gridWidth, _gridHeight));
        context.Barrier(_scratch);
    }

    private void RecordGrowthStage(
        in ComputeContext context,
        in DerivedValues derived,
        in Parameters parameters)
    {
        var gridWidth = _gridWidth;
        var gridHeight = _gridHeight;
        var gridLength = gridWidth * gridHeight;
        var cellSize = derived.CellSize;

        context.For(1, new InitScratchShader(_scratch));
        context.Barrier(_scratch);

        context.For(gridWidth, gridHeight, new EdgeSeedShader(
            _mask!, _luminance!, _jumpFloodA!, gridWidth, gridHeight, derived.EdgeThreshold));
        context.Barrier(_jumpFloodA!);
        var reading = _jumpFloodA!;
        var writing = _jumpFloodB!;
        var stepSize = InitialJumpFloodStep(gridWidth, gridHeight);
        while (stepSize >= 1)
        {
            context.For(gridWidth, gridHeight, new EdgeJumpFloodPassShader(reading, writing, gridWidth, gridHeight, stepSize, cellSize));
            context.Barrier(writing);
            (reading, writing) = (writing, reading);
            stepSize >>= 1;
        }
        context.For(gridWidth, gridHeight, new DirectionShader(
            reading, _mask!, _luminance!, _theta!, _edgeDistance!, gridWidth, gridHeight, cellSize, parameters.Align));
        context.Barrier(_theta!);
        context.Barrier(_edgeDistance!);

        context.For(gridWidth, gridHeight, new SpawnShader(
            _mask!, _theta!, _edgeDistance!, _sitePosX!, _sitePosY!, _siteTheta!, _siteSize!, _siteRank!, _scratch,
            gridWidth, gridHeight, cellSize, derived.Spacing, parameters.Detail, parameters.Seed));
        context.Barrier(_sitePosX!);
        context.Barrier(_sitePosY!);
        context.Barrier(_siteTheta!);
        context.Barrier(_siteSize!);
        context.Barrier(_siteRank!);
        context.Barrier(_scratch);

        for (var iteration = 0; iteration < derived.Iterations; iteration++)
        {
            RecordSiteVoronoi(in context, in derived, out reading);
            context.For(gridLength, new FillIntShader(_sumX!, gridLength, 0));
            context.For(gridLength, new FillIntShader(_sumY!, gridLength, 0));
            context.For(gridLength, new FillIntShader(_count!, gridLength, 0));
            context.Barrier(_sumX!);
            context.Barrier(_sumY!);
            context.Barrier(_count!);
            var avoidWidth = iteration < derived.Iterations - DecorativeMosaicSettings.OpenIterations ? derived.AvoidWidth : 0f;
            context.For(gridWidth, gridHeight, new CentroidAccumulateShader(
                reading, _mask!, _edgeDistance!, _sitePosX!, _sitePosY!, _sumX!, _sumY!, _count!,
                gridWidth, gridHeight, cellSize, avoidWidth, derived.Cutoff));
            context.Barrier(_sumX!);
            context.Barrier(_sumY!);
            context.Barrier(_count!);
            context.For(gridWidth, gridHeight, new SiteMoveShader(
                _siteRank!, _sitePosX!, _sitePosY!, _siteTheta!, _siteSize!, _sumX!, _sumY!, _count!,
                _theta!, _edgeDistance!, _scratch, gridWidth, gridHeight, cellSize, derived.Spacing, parameters.Detail));
            context.Barrier(_sitePosX!);
            context.Barrier(_sitePosY!);
            context.Barrier(_siteTheta!);
            context.Barrier(_siteSize!);
            context.Barrier(_siteRank!);
        }

        RecordSiteVoronoi(in context, in derived, out reading);
        context.For(gridLength, new CopyIntShader(reading, _siteMap!, gridLength));
        context.Barrier(_siteMap!);
        context.Barrier(_scratch);
    }

    private void RecordSiteVoronoi(in ComputeContext context, in DerivedValues derived, out ReadWriteBuffer<int> result)
    {
        var gridWidth = _gridWidth;
        var gridHeight = _gridHeight;
        var gridLength = gridWidth * gridHeight;
        context.For(gridLength, new FillIntShader(_jumpFloodA!, gridLength, DecorativeMosaicSettings.SiteSentinel));
        context.Barrier(_jumpFloodA!);
        context.For(gridWidth, gridHeight, new SiteSeedShader(
            _siteRank!, _sitePosX!, _sitePosY!, _jumpFloodA!, gridWidth, gridHeight, derived.CellSize));
        context.Barrier(_jumpFloodA!);
        var reading = _jumpFloodA!;
        var writing = _jumpFloodB!;
        var stepSize = InitialJumpFloodStep(gridWidth, gridHeight);
        while (stepSize >= 1)
        {
            context.For(gridWidth, gridHeight, new SiteJumpFloodPassShader(
                reading, writing, _sitePosX!, _sitePosY!, _siteTheta!, _siteSize!,
                gridWidth, gridHeight, stepSize, derived.CellSize, derived.Gamma));
            context.Barrier(writing);
            (reading, writing) = (writing, reading);
            stepSize >>= 1;
        }
        result = reading;
    }

    private void RecordRenderStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        PixelRect rect,
        int canvasWidth,
        int canvasHeight,
        in DerivedValues derived,
        in Parameters parameters)
    {
        context.For(rect.Width, rect.Height, new RenderShader(
            _siteMap!, _sitePosX!, _sitePosY!, _siteTheta!, _siteSize!, _siteRank!, source, output,
            rect.X, rect.Y, rect.Width, rect.Height, _gridWidth, _gridHeight,
            canvasWidth, canvasHeight, derived.CellSize, derived.Gamma, derived.HalfBase,
            DecorativeMosaicSettings.GetVisibleCount(parameters.Laying), parameters.Seed,
            Math.Clamp(parameters.Irregularity, 0f, 1f),
            Math.Clamp(parameters.ColorVariation, 0f, 1f),
            Math.Clamp(parameters.Bevel, 0f, 1f),
            parameters.GroutR, parameters.GroutG, parameters.GroutB,
            Math.Clamp(parameters.GroutOpacity, 0f, 1f)));
    }

    private static int InitialJumpFloodStep(int gridWidth, int gridHeight)
    {
        var maxSide = Math.Max(gridWidth, gridHeight);
        var stepSize = 1;
        while (stepSize < maxSide)
            stepSize <<= 1;
        return stepSize >> 1;
    }

    private void BuildBoundsPrefix()
    {
        for (var step = 0; step < DecorativeMosaicSettings.LayingSteps; step++)
        {
            _boundsMinX[step] = float.MaxValue;
            _boundsMinY[step] = float.MaxValue;
            _boundsMaxX[step] = float.MinValue;
            _boundsMaxY[step] = float.MinValue;
        }

        var ranks = _cachedRank!;
        var posX = _cachedPosX!;
        var posY = _cachedPosY!;
        for (var index = 0; index < ranks.Length; index++)
        {
            var rank = ranks[index];
            if (rank < 0 || rank >= DecorativeMosaicSettings.LayingSteps)
                continue;
            var x = posX[index];
            var y = posY[index];
            if (x < _boundsMinX[rank])
                _boundsMinX[rank] = x;
            if (x > _boundsMaxX[rank])
                _boundsMaxX[rank] = x;
            if (y < _boundsMinY[rank])
                _boundsMinY[rank] = y;
            if (y > _boundsMaxY[rank])
                _boundsMaxY[rank] = y;
        }

        for (var step = 1; step < DecorativeMosaicSettings.LayingSteps; step++)
        {
            _boundsMinX[step] = Math.Min(_boundsMinX[step], _boundsMinX[step - 1]);
            _boundsMinY[step] = Math.Min(_boundsMinY[step], _boundsMinY[step - 1]);
            _boundsMaxX[step] = Math.Max(_boundsMaxX[step], _boundsMaxX[step - 1]);
            _boundsMaxY[step] = Math.Max(_boundsMaxY[step], _boundsMaxY[step - 1]);
        }
    }

    private DerivedValues Derive(int width, int height, in Parameters parameters)
    {
        var settings = DecorativeMosaicSettings.GetQuality(parameters.Quality);
        var spacing = DecorativeMosaicSettings.GetSpacing(parameters.TileSize);
        var (_, _, cellSize) = DecorativeMosaicSettings.GetGridSize(width, height, spacing, in settings);
        return new DerivedValues(
            cellSize,
            spacing,
            DecorativeMosaicSettings.GetAspect(parameters.Aspect),
            DecorativeMosaicSettings.GetEdgeThreshold(parameters.EdgeDetect),
            settings.LloydIterations,
            DecorativeMosaicSettings.GetHalfBase(spacing, parameters.Grout),
            Math.Max(DecorativeMosaicSettings.EdgeAvoidFactor * spacing, cellSize),
            DecorativeMosaicSettings.CentroidCutoffFactor * spacing);
    }

    private void EnsureGridFor(int width, int height, in Parameters parameters)
    {
        var settings = DecorativeMosaicSettings.GetQuality(parameters.Quality);
        var spacing = DecorativeMosaicSettings.GetSpacing(parameters.TileSize);
        var (gridWidth, gridHeight, _) = DecorativeMosaicSettings.GetGridSize(width, height, spacing, in settings);
        EnsureGrid(gridWidth, gridHeight);
    }

    private void EnsureGrid(int gridWidth, int gridHeight)
    {
        if (_gridWidth == gridWidth && _gridHeight == gridHeight)
            return;

        DisposeGridBuffers();
        var gridLength = gridWidth * gridHeight;
        _mask = _device.AllocateReadWriteBuffer<int>(gridLength);
        _luminance = _device.AllocateReadWriteBuffer<float>(gridLength);
        _theta = _device.AllocateReadWriteBuffer<float>(gridLength);
        _edgeDistance = _device.AllocateReadWriteBuffer<float>(gridLength);
        _jumpFloodA = _device.AllocateReadWriteBuffer<int>(gridLength);
        _jumpFloodB = _device.AllocateReadWriteBuffer<int>(gridLength);
        _siteMap = _device.AllocateReadWriteBuffer<int>(gridLength);
        _sitePosX = _device.AllocateReadWriteBuffer<float>(gridLength);
        _sitePosY = _device.AllocateReadWriteBuffer<float>(gridLength);
        _siteTheta = _device.AllocateReadWriteBuffer<float>(gridLength);
        _siteSize = _device.AllocateReadWriteBuffer<float>(gridLength);
        _siteRank = _device.AllocateReadWriteBuffer<int>(gridLength);
        _sumX = _device.AllocateReadWriteBuffer<int>(gridLength);
        _sumY = _device.AllocateReadWriteBuffer<int>(gridLength);
        _count = _device.AllocateReadWriteBuffer<int>(gridLength);
        _posXReadBack = _device.AllocateReadBackBuffer<float>(gridLength);
        _posYReadBack = _device.AllocateReadBackBuffer<float>(gridLength);
        _rankReadBack = _device.AllocateReadBackBuffer<int>(gridLength);
        _cachedPosX = new float[gridLength];
        _cachedPosY = new float[gridLength];
        _cachedRank = new int[gridLength];
        _cachedSiteCount = 0;
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;

        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    private void DisposeGridBuffers()
    {
        _mask?.Dispose();
        _luminance?.Dispose();
        _theta?.Dispose();
        _edgeDistance?.Dispose();
        _jumpFloodA?.Dispose();
        _jumpFloodB?.Dispose();
        _siteMap?.Dispose();
        _sitePosX?.Dispose();
        _sitePosY?.Dispose();
        _siteTheta?.Dispose();
        _siteSize?.Dispose();
        _siteRank?.Dispose();
        _sumX?.Dispose();
        _sumY?.Dispose();
        _count?.Dispose();
        _posXReadBack?.Dispose();
        _posYReadBack?.Dispose();
        _rankReadBack?.Dispose();
        _mask = null;
        _luminance = null;
        _theta = null;
        _edgeDistance = null;
        _jumpFloodA = null;
        _jumpFloodB = null;
        _siteMap = null;
        _sitePosX = null;
        _sitePosY = null;
        _siteTheta = null;
        _siteSize = null;
        _siteRank = null;
        _sumX = null;
        _sumY = null;
        _count = null;
        _posXReadBack = null;
        _posYReadBack = null;
        _rankReadBack = null;
        _cachedPosX = null;
        _cachedPosY = null;
        _cachedRank = null;
        _cachedSiteCount = 0;
        _structureKey = null;
        _gridWidth = 0;
        _gridHeight = 0;
    }

    public void Dispose()
    {
        DisposeGridBuffers();
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _scratchReadBack.Dispose();
        _scratch.Dispose();
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

    private readonly record struct StructureKey(
        int MaskHashSum,
        int MaskHashMix,
        int CanvasWidth,
        int CanvasHeight,
        DecorativeMosaicQuality Quality,
        int Seed,
        float TileSize,
        float Aspect,
        float Align,
        float EdgeDetect,
        float Detail);

    private readonly record struct DerivedValues(
        float CellSize,
        float Spacing,
        float Gamma,
        float EdgeThreshold,
        int Iterations,
        float HalfBase,
        float AvoidWidth,
        float Cutoff);

    internal readonly record struct Parameters(
        DecorativeMosaicQuality Quality,
        float Laying,
        float TileSize,
        float Grout,
        float Aspect,
        float Align,
        float EdgeDetect,
        float Detail,
        float Irregularity,
        float ColorVariation,
        float Bevel,
        float GroutR,
        float GroutG,
        float GroutB,
        float GroutOpacity,
        int Seed);
}
