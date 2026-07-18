using System.Diagnostics;
using ComputeSharp;
using DecorativeMosaic;

var width = 1280;
var height = 720;
var outputDirectory = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : Path.Combine(AppContext.BaseDirectory, "harness-output");
Directory.CreateDirectory(outputDirectory);

using var pipeline = DecorativeMosaicPipeline.TryCreate();
if (pipeline is null)
{
    Console.WriteLine("Direct3D 12 is unavailable.");
    return 1;
}

var source = CreateTestImage(width, height);
var destination = new int[source.Length];

if (args.Contains("--golden"))
{
    var goldenCases = new (string Name, DecorativeMosaicPipeline.Parameters Parameters)[]
    {
        ("balanced-default", new(DecorativeMosaicQuality.Balanced, 1f, 14f, 0.3f, 0f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7)),
        ("high-default", new(DecorativeMosaicQuality.High, 1f, 14f, 0.3f, 0f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7)),
        ("seed-42", new(DecorativeMosaicQuality.Balanced, 1f, 14f, 0.3f, 0f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 42)),
        ("aspect-max", new(DecorativeMosaicQuality.Balanced, 1f, 14f, 0.3f, 1f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7)),
        ("align-zero", new(DecorativeMosaicQuality.Balanced, 1f, 14f, 0.3f, 0f, 0f, 0f, 0f, 0f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7)),
        ("grout-wide", new(DecorativeMosaicQuality.Balanced, 1f, 14f, 1f, 0f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7)),
        ("detail-max", new(DecorativeMosaicQuality.Balanced, 1f, 20f, 0.3f, 0f, 1f, 1f, 1f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7)),
        ("partial-laying", new(DecorativeMosaicQuality.Balanced, 0.35f, 14f, 0.3f, 0f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7)),
    };
    foreach (var (name, goldenParameters) in goldenCases)
    {
        var parameters = goldenParameters;
        pipeline.Process(source, destination, width, height, in parameters);
        var bytes = new byte[destination.Length * sizeof(int)];
        Buffer.BlockCopy(destination, 0, bytes, 0, bytes.Length);
        Console.WriteLine($"{name}: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))}");
    }
    return 0;
}

foreach (var quality in new[] { DecorativeMosaicQuality.Balanced, DecorativeMosaicQuality.High, DecorativeMosaicQuality.Ultra })
{
    var parameters = new DecorativeMosaicPipeline.Parameters(quality, 1f, 14f, 0.3f, 0f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    pipeline.Process(source, destination, width, height, in parameters);
    var stopwatch = Stopwatch.StartNew();
    const int frames = 5;
    for (var frame = 0; frame < frames; frame++)
        pipeline.Process(source, destination, width, height, in parameters);
    stopwatch.Stop();
    Console.WriteLine($"{quality}: {stopwatch.Elapsed.TotalMilliseconds / frames:F2} ms/frame ({width}x{height}) litPixels={CountLit(destination)}");
}

{
    var device = GraphicsDevice.GetDefault();
    using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
    var pixels = new Bgra32[source.Length];
    for (var index = 0; index < source.Length; index++)
        pixels[index].PackedValue = unchecked((uint)source[index]);
    sourceTexture.CopyFrom(pixels);
    var parameters = new DecorativeMosaicPipeline.Parameters(DecorativeMosaicQuality.High, 1f, 14f, 0.3f, 0f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7);

    pipeline.Simulate(sourceTexture, width, height, in parameters);
    var stopwatch = Stopwatch.StartNew();
    pipeline.Simulate(sourceTexture, width, height, parameters with { Seed = 8 });
    stopwatch.Stop();
    Console.WriteLine($"structure recompute: {stopwatch.Elapsed.TotalMilliseconds:F2} ms");

    if (pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect))
    {
        using var rectOutput = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(sourceTexture, rectOutput, width, height, rect, in parameters);
        pipeline.WaitForCompletion();
        stopwatch.Restart();
        const int rectFrames = 20;
        for (var frame = 0; frame < rectFrames; frame++)
        {
            pipeline.Simulate(sourceTexture, width, height, in parameters);
            pipeline.TryGetVisibleBounds(width, height, in parameters, out rect);
            pipeline.RenderVisible(sourceTexture, rectOutput, width, height, rect, in parameters);
        }
        pipeline.WaitForCompletion();
        stopwatch.Stop();
        Console.WriteLine($"cached frame with rect {rect.Width}x{rect.Height} at ({rect.X},{rect.Y}): {stopwatch.Elapsed.TotalMilliseconds / rectFrames:F2} ms/frame");
    }
}

foreach (var laying in new[] { 0.25f, 0.5f, 0.75f, 1f })
{
    var parameters = new DecorativeMosaicPipeline.Parameters(DecorativeMosaicQuality.High, laying, 14f, 0.3f, 0f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"laying={laying:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"laying{(int)(laying * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var tileSize in new[] { 8f, 14f, 28f })
{
    var parameters = new DecorativeMosaicPipeline.Parameters(DecorativeMosaicQuality.High, 1f, tileSize, 0.3f, 0f, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"tileSize={tileSize:F0} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"tilesize{(int)tileSize:D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var aspect in new[] { 0f, 0.5f, 1f })
{
    var parameters = new DecorativeMosaicPipeline.Parameters(DecorativeMosaicQuality.High, 1f, 14f, 0.3f, aspect, 1f, 0.5f, 0f, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"aspect={aspect:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"aspect{(int)(aspect * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var detail in new[] { 0f, 1f })
{
    var parameters = new DecorativeMosaicPipeline.Parameters(DecorativeMosaicQuality.High, 1f, 20f, 0.3f, 0f, 1f, 1f, detail, 0.25f, 0.2f, 0.35f, 0.82f, 0.78f, 0.73f, 1f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"detail={detail:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"detail{(int)(detail * 100):D3}.bmp"), Composite(source, destination), width, height);
}

WriteBmp(Path.Combine(outputDirectory, "source.bmp"), source, width, height);
Console.WriteLine($"images written to {outputDirectory}");
return 0;

static int[] CreateTestImage(int width, int height)
{
    var pixels = new int[width * height];
    var centerX = width / 2;
    var centerY = height / 2;
    var radius = 240;
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var dx = x - centerX;
            var dy = y - centerY;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > radius)
                continue;
            var inner = distance < radius * 0.45;
            var r = inner ? 176 : 64;
            var g = inner ? 112 : 96;
            var b = inner ? 64 : 160;
            pixels[y * width + x] = unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
        }
    }
    return pixels;
}

static int CountLit(int[] pixels)
{
    var count = 0;
    foreach (var pixel in pixels)
    {
        if (((pixel >> 24) & 255) > 8)
            count++;
    }
    return count;
}

static int[] Composite(int[] source, int[] mosaic)
{
    var result = new int[source.Length];
    for (var index = 0; index < source.Length; index++)
    {
        var s = source[index];
        var m = mosaic[index];
        var sa = (s >> 24) & 255;
        var ma = (m >> 24) & 255;
        var a = Math.Min(ma + sa * (255 - ma) / 255, 255);
        var r = Over((s >> 16) & 255, (m >> 16) & 255, ma);
        var g = Over((s >> 8) & 255, (m >> 8) & 255, ma);
        var b = Over(s & 255, m & 255, ma);
        result[index] = a << 24 | r << 16 | g << 8 | b;
    }
    return result;

    static int Over(int s, int m, int ma) => Math.Min(m + s * (255 - ma) / 255, 255);
}

static void WriteBmp(string path, int[] pixels, int width, int height)
{
    var stride = width * 3;
    var padding = (4 - stride % 4) % 4;
    var dataSize = (stride + padding) * height;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var writer = new BinaryWriter(stream);
    writer.Write((byte)'B');
    writer.Write((byte)'M');
    writer.Write(54 + dataSize);
    writer.Write(0);
    writer.Write(54);
    writer.Write(40);
    writer.Write(width);
    writer.Write(height);
    writer.Write((short)1);
    writer.Write((short)24);
    writer.Write(0);
    writer.Write(dataSize);
    writer.Write(2835);
    writer.Write(2835);
    writer.Write(0);
    writer.Write(0);
    var pad = new byte[padding];
    for (var y = height - 1; y >= 0; y--)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = pixels[y * width + x];
            writer.Write((byte)(pixel & 255));
            writer.Write((byte)((pixel >> 8) & 255));
            writer.Write((byte)((pixel >> 16) & 255));
        }
        writer.Write(pad);
    }
}
