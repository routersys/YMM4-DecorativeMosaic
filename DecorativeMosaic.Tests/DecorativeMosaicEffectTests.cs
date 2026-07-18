using System.Runtime.InteropServices;
using ComputeSharp;
using ComputeSharp.Interop;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace DecorativeMosaic.Tests;

public sealed class DecorativeMosaicEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

    private static DecorativeMosaicPipeline.Parameters CreateParameters(
        DecorativeMosaicQuality quality = DecorativeMosaicQuality.Balanced,
        float laying = 1f,
        float tileSize = 12f,
        float grout = 0.3f,
        float aspect = 0f,
        float align = 1f,
        float edgeDetect = 0.5f,
        float detail = 0f,
        float irregularity = 0.25f,
        float colorVariation = 0.2f,
        float bevel = 0.35f,
        float groutOpacity = 1f,
        int seed = 0)
        => new(quality, laying, tileSize, grout, aspect, align, edgeDetect, detail, irregularity, colorVariation, bevel, 0.82f, 0.78f, 0.73f, groutOpacity, seed);

    [Fact]
    public void DefaultParameterValuesMatchSpecification()
    {
        var effect = new DecorativeMosaicEffect();

        Assert.Equal(100d, ValueAt(effect.Amount), 6);
        Assert.Equal(100d, ValueAt(effect.Laying), 6);
        Assert.Equal(14d, ValueAt(effect.TileSize), 6);
        Assert.Equal(30d, ValueAt(effect.Grout), 6);
        Assert.Equal(0d, ValueAt(effect.Aspect), 6);
        Assert.Equal(25d, ValueAt(effect.Irregularity), 6);
        Assert.Equal(0d, ValueAt(effect.Detail), 6);
        Assert.Equal(100d, ValueAt(effect.Align), 6);
        Assert.Equal(50d, ValueAt(effect.EdgeDetect), 6);
        Assert.Equal(35d, ValueAt(effect.Bevel), 6);
        Assert.Equal(20d, ValueAt(effect.ColorVariation), 6);
        Assert.Equal(100d, ValueAt(effect.GroutOpacity), 6);
        Assert.Equal(DecorativeMosaicQuality.High, effect.Quality);
        Assert.Equal(0, effect.Seed);
        Assert.Equal(System.Windows.Media.Color.FromArgb(255, 209, 200, 186), effect.GroutColor);
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1234, 1234)]
    public void SeedClampsNegativeInputToZero(int input, int expected)
    {
        var effect = new DecorativeMosaicEffect { Seed = input };

        Assert.Equal(expected, effect.Seed);
    }

    [Fact]
    public void CreateExoVideoFiltersReturnsEmpty()
    {
        var effect = new DecorativeMosaicEffect();

        Assert.Empty(effect.CreateExoVideoFilters(0, null!));
    }

    [Theory]
    [InlineData(DecorativeMosaicQuality.Balanced, 5, 10, 512)]
    [InlineData(DecorativeMosaicQuality.High, 6, 18, 768)]
    [InlineData(DecorativeMosaicQuality.Ultra, 8, 26, 1024)]
    public void QualitySettingsMatchSpecification(DecorativeMosaicQuality quality, int cellsPerTile, int iterations, int maxResolution)
    {
        var settings = DecorativeMosaicSettings.GetQuality(quality);

        Assert.Equal(cellsPerTile, settings.CellsPerTile);
        Assert.Equal(iterations, settings.LloydIterations);
        Assert.Equal(maxResolution, settings.MaximumGridResolution);
    }

    [Theory]
    [InlineData(1920, 1080, 12f, DecorativeMosaicQuality.High)]
    [InlineData(1080, 1920, 12f, DecorativeMosaicQuality.High)]
    [InlineData(8, 8, 2f, DecorativeMosaicQuality.Balanced)]
    [InlineData(4096, 16, 200f, DecorativeMosaicQuality.Ultra)]
    [InlineData(100, 100, 40f, DecorativeMosaicQuality.Ultra)]
    public void GridSizeCoversCanvas(int width, int height, float tileSize, DecorativeMosaicQuality quality)
    {
        var settings = DecorativeMosaicSettings.GetQuality(quality);
        var spacing = DecorativeMosaicSettings.GetSpacing(tileSize);
        var (gridWidth, gridHeight, cellSize) = DecorativeMosaicSettings.GetGridSize(width, height, spacing, in settings);

        Assert.True(gridWidth >= DecorativeMosaicSettings.MinimumGridSize);
        Assert.True(gridHeight >= DecorativeMosaicSettings.MinimumGridSize);
        Assert.True(cellSize > 0f);
        Assert.True(gridWidth * cellSize >= width);
        Assert.True(gridHeight * cellSize >= height);
    }

    [Fact]
    public void ParameterMappingsAreMonotonicAndBounded()
    {
        Assert.Equal(DecorativeMosaicSettings.MinimumTileSize, DecorativeMosaicSettings.GetSpacing(0f), 5);
        Assert.Equal(DecorativeMosaicSettings.MaximumTileSize, DecorativeMosaicSettings.GetSpacing(1000f), 5);
        Assert.Equal(12f, DecorativeMosaicSettings.GetSpacing(12f), 5);

        Assert.Equal(1f, DecorativeMosaicSettings.GetAspect(0f), 5);
        Assert.Equal(DecorativeMosaicSettings.MaximumAspect, DecorativeMosaicSettings.GetAspect(1f), 5);
        Assert.Equal(1f, DecorativeMosaicSettings.GetAspect(-2f), 5);
        Assert.Equal(DecorativeMosaicSettings.MaximumAspect, DecorativeMosaicSettings.GetAspect(2f), 5);
        Assert.True(DecorativeMosaicSettings.GetAspect(0.75f) > DecorativeMosaicSettings.GetAspect(0.25f));

        Assert.Equal(DecorativeMosaicSettings.EdgeDisabledThreshold, DecorativeMosaicSettings.GetEdgeThreshold(0f), 1);
        Assert.Equal(DecorativeMosaicSettings.MinimumEdgeThreshold, DecorativeMosaicSettings.GetEdgeThreshold(1f), 5);
        Assert.True(DecorativeMosaicSettings.GetEdgeThreshold(0.75f) < DecorativeMosaicSettings.GetEdgeThreshold(0.25f));

        Assert.True(DecorativeMosaicSettings.GetHalfBase(12f, 0f) > DecorativeMosaicSettings.GetHalfBase(12f, 1f));
        Assert.True(DecorativeMosaicSettings.GetHalfBase(12f, 1f) > 0f);
        Assert.True(DecorativeMosaicSettings.GetHalfBase(12f, 0f) * 2f < 12f);
    }

    [Theory]
    [InlineData(-1f, 0)]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 512)]
    [InlineData(1f, 1024)]
    [InlineData(2f, 1024)]
    public void VisibleCountClampsAndScales(float laying, int expected)
    {
        Assert.Equal(expected, DecorativeMosaicSettings.GetVisibleCount(laying));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(256, 128, 8)]
    [InlineData(257, 16, 9)]
    public void JumpFloodPassCountCoversLongSide(int width, int height, int expected)
    {
        Assert.Equal(expected, DecorativeMosaicSettings.GetJumpFloodPassCount(width, height));
    }

    [Fact]
    public void TransparentInputYieldsTransparentOutput()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = new int[width * height];
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void LayingZeroYieldsTransparentOutput()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = CreateSquareSource(width, height, 16, 16, 64, 64);
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters(laying: 0f);

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void FullLayingProducesTiles()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 16, 16, 96, 96);
        var destination = new int[source.Length];
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.True(CountLitPixels(destination) > 0);
    }

    [Fact]
    public void GpuPipelineIsDeterministic()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 16, 16, 96, 96);
        var first = new int[source.Length];
        var second = new int[source.Length];
        var parameters = CreateParameters(irregularity: 0.8f, seed: 42);

        pipeline.Process(source, first, width, height, in parameters);
        pipeline.Process(source, second, width, height, in parameters);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentMosaics()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 16, 16, 96, 96);
        var first = new int[source.Length];
        var second = new int[source.Length];

        var parametersA = CreateParameters(seed: 1);
        var parametersB = CreateParameters(seed: 2);
        pipeline.Process(source, first, width, height, in parametersA);
        pipeline.Process(source, second, width, height, in parametersB);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OutputAlphaStaysPremultipliedAndBounded()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 16, 16, 96, 96);
        var destination = new int[source.Length];
        var parameters = CreateParameters(bevel: 1f, colorVariation: 1f, irregularity: 1f);

        pipeline.Process(source, destination, width, height, in parameters);

        foreach (var pixel in destination)
        {
            var alpha = (pixel >> 24) & 255;
            Assert.InRange((pixel >> 16) & 255, 0, alpha);
            Assert.InRange((pixel >> 8) & 255, 0, alpha);
            Assert.InRange(pixel & 255, 0, alpha);
        }
    }

    [Fact]
    public void LayingIncreasesLitArea()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 16, 16, 96, 96);
        var partial = new int[source.Length];
        var full = new int[source.Length];

        var partialParameters = CreateParameters(laying: 0.2f);
        var fullParameters = CreateParameters(laying: 1f);
        pipeline.Process(source, partial, width, height, in partialParameters);
        pipeline.Process(source, full, width, height, in fullParameters);

        Assert.True(CountLitPixels(full) > CountLitPixels(partial));
    }

    [Fact]
    public void OutputStaysInsideSilhouette()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 160;
        const int height = 160;
        const int left = 32;
        const int top = 40;
        const int squareWidth = 80;
        const int squareHeight = 72;
        var source = CreateSquareSource(width, height, left, top, squareWidth, squareHeight);
        var destination = new int[source.Length];
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (destination[y * width + x] == 0)
                    continue;
                Assert.InRange(x, left, left + squareWidth - 1);
                Assert.InRange(y, top, top + squareHeight - 1);
            }
        }
    }

    [Fact]
    public void SmallerTilesIncreaseTileCount()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 160;
        const int height = 160;
        var source = CreateSquareSource(width, height, 8, 8, 144, 144);
        var small = new int[source.Length];
        var large = new int[source.Length];

        var smallParameters = CreateParameters(tileSize: 8f, grout: 0.6f, irregularity: 0f, bevel: 0f);
        var largeParameters = CreateParameters(tileSize: 32f, grout: 0.6f, irregularity: 0f, bevel: 0f);
        pipeline.Process(source, small, width, height, in smallParameters);
        pipeline.Process(source, large, width, height, in largeParameters);

        var smallTransitions = CountTileGroutTransitions(small, width, height);
        var largeTransitions = CountTileGroutTransitions(large, width, height);
        Assert.True(smallTransitions > 0);
        Assert.True(largeTransitions > 0);
        Assert.True(smallTransitions > largeTransitions);
    }

    [Fact]
    public void GpuPipelineDoesNotAllocateManagedMemoryAfterWarmup()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = CreateSquareSource(width, height, 8, 8, 48, 48);
        var destination = new int[source.Length];
        var parameters = CreateParameters();
        pipeline.Process(source, destination, width, height, in parameters);
        pipeline.Process(source, destination, width, height, in parameters);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.Process(source, destination, width, height, in parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SharedTexturePipelineMatchesPackedBufferPipeline()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = CreateSquareSource(width, height, 8, 8, 80, 80);
        var expected = new int[source.Length];
        var parameters = CreateParameters(seed: 11);
        pipeline.Process(source, expected, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);
        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, width, height, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(unchecked((uint)expected[index]), result[index].PackedValue);
    }

    [Fact]
    public void SubmittedSharedTexturePipelineAllocationsAmortizeToZero()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, width, height, in parameters);
        pipeline.WaitForCompletion();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, width, height, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        pipeline.WaitForCompletion();

        Assert.Equal(0, minimum);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(1f)]
    public void VisibleBoundsCoverAllLitPixelsAndMatchFullRender(float laying)
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 192;
        const int height = 192;
        var source = CreateSquareSource(width, height, 48, 56, 96, 80);
        var full = new int[source.Length];
        var parameters = CreateParameters(laying: laying, seed: 5);
        pipeline.Process(source, full, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        pipeline.Simulate(sourceTexture, width, height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect));
        Assert.True(rect.Width > 0 && rect.Height > 0);
        Assert.True(rect.X >= 0 && rect.Y >= 0);
        Assert.True(rect.X + rect.Width <= width && rect.Y + rect.Height <= height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (full[y * width + x] == 0)
                    continue;
                Assert.InRange(x, rect.X, rect.X + rect.Width - 1);
                Assert.InRange(y, rect.Y, rect.Y + rect.Height - 1);
            }
        }

        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(sourceTexture, outputTexture, width, height, rect, in parameters);
        var result = new Bgra32[rect.Width * rect.Height];
        outputTexture.CopyTo(result);

        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                var expected = unchecked((uint)full[(rect.Y + y) * width + rect.X + x]);
                Assert.Equal(expected, result[y * rect.Width + x].PackedValue);
            }
        }
    }

    [Fact]
    public void SimulateCachesStructureUntilInputsChange()
    {
        using var pipeline = DecorativeMosaicPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 16, 16, 96, 96);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        var parameters = CreateParameters(seed: 3);
        Assert.True(pipeline.Simulate(sourceTexture, width, height, in parameters));
        Assert.False(pipeline.Simulate(sourceTexture, width, height, in parameters));

        var layingChanged = parameters with { Laying = 0.5f };
        Assert.False(pipeline.Simulate(sourceTexture, width, height, in layingChanged));

        var groutChanged = parameters with { Grout = 0.8f };
        Assert.False(pipeline.Simulate(sourceTexture, width, height, in groutChanged));

        var seedChanged = parameters with { Seed = 4 };
        Assert.True(pipeline.Simulate(sourceTexture, width, height, in seedChanged));

        var tileSizeChanged = parameters with { Seed = 4, TileSize = 20f };
        Assert.True(pipeline.Simulate(sourceTexture, width, height, in tileSizeChanged));

        var movedSource = CreateSquareSource(width, height, 8, 8, 96, 96);
        for (var index = 0; index < movedSource.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)movedSource[index]);
        sourceTexture.CopyFrom(sourcePixels);
        Assert.True(pipeline.Simulate(sourceTexture, width, height, in tileSizeChanged));
    }

    [Fact]
    public void Direct2DInteropProducesMosaicFromOpaqueCore()
    {
        using var devices = new GraphicsDevices();
        using var graphicsContext = devices.CreateContext();
        using var interop = DecorativeMosaicGpuInterop.TryCreate(graphicsContext);
        if (interop is null)
        {
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            return;
        }

        using var pipeline = DecorativeMosaicPipeline.TryCreate(interop.Device);
        Assert.NotNull(pipeline);

        const int width = 96;
        const int height = 96;
        var pixels = CreateSquareSource(width, height, 8, 8, 80, 80);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        using var inputBitmap = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.None));
        try
        {
            inputBitmap.CopyFromMemory(handle.AddrOfPinnedObject(), width * sizeof(int));
        }
        finally
        {
            handle.Free();
        }

        Assert.True(interop.EnsureResources(width, height));
        var bounds = new RawRectF(0f, 0f, width, height);
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 2; iteration++)
        {
            interop.RenderInput(inputBitmap, bounds);
            interop.BeginCompute();
            try
            {
                pipeline!.Process(interop.SourceTexture, interop.OutputTexture, width, height, in parameters);
            }
            finally
            {
                interop.EndCompute();
            }
        }
        interop.WaitForIdle();

        using var staging = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        staging.CopyFromBitmap(interop.OutputBitmap);
        var mapped = staging.Map(MapOptions.Read);
        try
        {
            var lit = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var actual = Marshal.ReadInt32(mapped.Bits + (nint)(y * mapped.Pitch + x * sizeof(int)));
                    var alpha = (actual >> 24) & 255;
                    Assert.InRange((actual >> 16) & 255, 0, alpha);
                    Assert.InRange((actual >> 8) & 255, 0, alpha);
                    Assert.InRange(actual & 255, 0, alpha);
                    if (alpha > 0)
                        lit++;
                }
            }
            Assert.True(lit > 0);
        }
        finally
        {
            staging.Unmap();
        }
    }

    private static int[] CreateSquareSource(int width, int height, int left, int top, int squareWidth, int squareHeight)
    {
        var source = new int[width * height];
        for (var y = top; y < top + squareHeight; y++)
        {
            for (var x = left; x < left + squareWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                source[y * width + x] = unchecked((int)0xFFC08040);
            }
        }
        return source;
    }

    private static int CountLitPixels(int[] pixels)
    {
        var count = 0;
        foreach (var pixel in pixels)
        {
            if (((pixel >> 24) & 255) > 8)
                count++;
        }
        return count;
    }

    private static bool IsGroutPixel(int pixel)
    {
        if (((pixel >> 24) & 255) <= 8)
            return false;
        var g = (pixel >> 8) & 255;
        return g > 140;
    }

    private static int CountTileGroutTransitions(int[] pixels, int width, int height)
    {
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 1; x < width; x++)
            {
                var current = pixels[y * width + x];
                var previous = pixels[y * width + x - 1];
                if (((current >> 24) & 255) <= 8 || ((previous >> 24) & 255) <= 8)
                    continue;
                if (IsGroutPixel(current) != IsGroutPixel(previous))
                    count++;
            }
        }
        return count;
    }
}
