using ComputeSharp;

namespace DecorativeMosaic;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillIntShader(
    ReadWriteBuffer<int> values,
    int length,
    int value) : IComputeShader
{
    private readonly ReadWriteBuffer<int> values = values;
    private readonly int length = length;
    private readonly int value = value;

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= length)
            return;
        values[index] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitScratchShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[0] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct AnalyzeShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<int> mask,
    ReadWriteBuffer<float> luminance,
    int sourceWidth,
    int sourceHeight,
    int gridWidth,
    int gridHeight,
    float cellSize,
    float alphaThreshold) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<int> mask = mask;
    private readonly ReadWriteBuffer<float> luminance = luminance;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;
    private readonly float alphaThreshold = alphaThreshold;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var x0 = Hlsl.Max((int)(gx * cellSize), 0);
        var x1 = Hlsl.Min((int)Hlsl.Ceil((gx + 1) * cellSize), sourceWidth);
        var y0 = Hlsl.Max((int)(gy * cellSize), 0);
        var y1 = Hlsl.Min((int)Hlsl.Ceil((gy + 1) * cellSize), sourceHeight);

        var found = 0;
        var lumaSum = 0f;
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var pixel = source[new Int2(x, y)];
                if (pixel.W > alphaThreshold)
                    found = 1;
                lumaSum += pixel.X * 0.299f + pixel.Y * 0.587f + pixel.Z * 0.114f;
                count++;
            }
        }

        var index = gy * gridWidth + gx;
        mask[index] = found;
        luminance[index] = count > 0 ? lumaSum / count : 0f;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MaskHashResetShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[6] = 0;
        scratch[7] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MaskHashShader(
    ReadWriteBuffer<int> mask,
    ReadWriteBuffer<float> luminance,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<int> mask = mask;
    private readonly ReadWriteBuffer<float> luminance = luminance;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (mask[index] != 1)
            return;

        var quantized = (int)(Hlsl.Saturate(luminance[index]) * 7.999f);
        var mixed = (uint)(index * 8 + quantized) * 0x9E3779B9u;
        mixed ^= mixed >> 16;
        mixed *= 0x85EBCA6Bu;
        mixed ^= mixed >> 13;
        Hlsl.InterlockedAdd(ref scratch[6], (int)mixed);
        Hlsl.InterlockedXor(ref scratch[7], (int)(mixed * 0xC2B2AE35u));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct EdgeSeedShader(
    ReadWriteBuffer<int> mask,
    ReadWriteBuffer<float> luminance,
    ReadWriteBuffer<int> jumpFlood,
    int gridWidth,
    int gridHeight,
    float edgeThreshold) : IComputeShader
{
    private readonly ReadWriteBuffer<int> mask = mask;
    private readonly ReadWriteBuffer<float> luminance = luminance;
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float edgeThreshold = edgeThreshold;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (mask[index] != 1)
        {
            jumpFlood[index] = -1;
            return;
        }

        var boundary = false;
        if (gx <= 0 || gx >= gridWidth - 1 || gy <= 0 || gy >= gridHeight - 1)
            boundary = true;
        else if (mask[index - 1] == 0 || mask[index + 1] == 0 || mask[index - gridWidth] == 0 || mask[index + gridWidth] == 0)
            boundary = true;

        var isEdge = boundary;
        if (!isEdge && edgeThreshold < 1e8f)
        {
            var left = luminance[index - 1];
            var right = luminance[index + 1];
            var up = luminance[index - gridWidth];
            var down = luminance[index + gridWidth];
            if (Hlsl.Abs(right - left) + Hlsl.Abs(down - up) > edgeThreshold)
                isEdge = true;
        }

        jumpFlood[index] = isEdge ? index : -1;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct EdgeJumpFloodPassShader(
    ReadWriteBuffer<int> input,
    ReadWriteBuffer<int> output,
    int gridWidth,
    int gridHeight,
    int stepSize,
    float cellSize) : IComputeShader
{
    private readonly ReadWriteBuffer<int> input = input;
    private readonly ReadWriteBuffer<int> output = output;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int stepSize = stepSize;
    private readonly float cellSize = cellSize;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= gridWidth || y >= gridHeight)
            return;

        var position = DecorativeMosaicShaderMath.CellCenter(x, y, cellSize);
        var best = -1;
        var bestDistance = 3.402823e+38f;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = x + dx * stepSize;
                var sy = y + dy * stepSize;
                if (sx < 0 || sx >= gridWidth || sy < 0 || sy >= gridHeight)
                    continue;
                var candidate = input[sy * gridWidth + sx];
                if (candidate < 0)
                    continue;
                var candidateCenter = DecorativeMosaicShaderMath.CellCenter(candidate % gridWidth, candidate / gridWidth, cellSize);
                var delta = position - candidateCenter;
                var distance = delta.X * delta.X + delta.Y * delta.Y;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }
        output[y * gridWidth + x] = best;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct DirectionShader(
    ReadWriteBuffer<int> jumpFlood,
    ReadWriteBuffer<int> mask,
    ReadWriteBuffer<float> luminance,
    ReadWriteBuffer<float> theta,
    ReadWriteBuffer<float> edgeDistance,
    int gridWidth,
    int gridHeight,
    float cellSize,
    float align) : IComputeShader
{
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly ReadWriteBuffer<int> mask = mask;
    private readonly ReadWriteBuffer<float> luminance = luminance;
    private readonly ReadWriteBuffer<float> theta = theta;
    private readonly ReadWriteBuffer<float> edgeDistance = edgeDistance;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;
    private readonly float align = align;

    private float FieldValue(int x, int y)
    {
        var cx = Hlsl.Clamp(x, 0, gridWidth - 1);
        var cy = Hlsl.Clamp(y, 0, gridHeight - 1);
        var index = cy * gridWidth + cx;
        return mask[index] + luminance[index];
    }

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= gridWidth || y >= gridHeight)
            return;

        var index = y * gridWidth + x;
        var seed = jumpFlood[index];
        if (seed < 0)
        {
            theta[index] = 0f;
            edgeDistance[index] = 1e9f;
            return;
        }

        var position = DecorativeMosaicShaderMath.CellCenter(x, y, cellSize);
        var seedCenter = DecorativeMosaicShaderMath.CellCenter(seed % gridWidth, seed / gridWidth, cellSize);
        var delta = position - seedCenter;
        var distance = Hlsl.Sqrt(delta.X * delta.X + delta.Y * delta.Y);

        float tangent;
        if (distance < cellSize)
        {
            var gx = FieldValue(x + 1, y) - FieldValue(x - 1, y);
            var gy = FieldValue(x, y + 1) - FieldValue(x, y - 1);
            tangent = Hlsl.Abs(gx) + Hlsl.Abs(gy) < 1e-5f ? 0f : Hlsl.Atan2(gy, gx) + 1.5707964f;
        }
        else
        {
            tangent = Hlsl.Atan2(delta.Y, delta.X) + 1.5707964f;
        }

        var blendX = align * Hlsl.Cos(4f * tangent) + (1f - align);
        var blendY = align * Hlsl.Sin(4f * tangent);
        theta[index] = Hlsl.Atan2(blendY, blendX) * 0.25f;
        edgeDistance[index] = distance;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SpawnShader(
    ReadWriteBuffer<int> mask,
    ReadWriteBuffer<float> theta,
    ReadWriteBuffer<float> edgeDistance,
    ReadWriteBuffer<Float4> site,
    ReadWriteBuffer<Float2> siteReference,
    ReadWriteBuffer<int> siteRank,
    ReadWriteBuffer<int> sumX,
    ReadWriteBuffer<int> sumY,
    ReadWriteBuffer<int> count,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight,
    float cellSize,
    float spacing,
    float detail,
    int seed) : IComputeShader
{
    private readonly ReadWriteBuffer<int> mask = mask;
    private readonly ReadWriteBuffer<float> theta = theta;
    private readonly ReadWriteBuffer<float> edgeDistance = edgeDistance;
    private readonly ReadWriteBuffer<Float4> site = site;
    private readonly ReadWriteBuffer<Float2> siteReference = siteReference;
    private readonly ReadWriteBuffer<int> siteRank = siteRank;
    private readonly ReadWriteBuffer<int> sumX = sumX;
    private readonly ReadWriteBuffer<int> sumY = sumY;
    private readonly ReadWriteBuffer<int> count = count;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;
    private readonly float spacing = spacing;
    private readonly float detail = detail;
    private readonly int seed = seed;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        sumX[index] = 0;
        sumY[index] = 0;
        count[index] = 0;
        if (mask[index] != 1)
        {
            siteRank[index] = -1;
            return;
        }

        var sizeFactor = DecorativeMosaicShaderMath.SizeFactor(edgeDistance[index], spacing, detail);
        var local = spacing * sizeFactor;
        var probability = cellSize * cellSize / (local * local);
        var basis = (uint)index * 0x9E3779B9u ^ (uint)seed * 0x85EBCA6Bu;
        if (DecorativeMosaicShaderMath.Hash01(basis) >= probability)
        {
            siteRank[index] = -1;
            return;
        }

        var center = DecorativeMosaicShaderMath.CellCenter(gx, gy, cellSize);
        var positionX = center.X + (DecorativeMosaicShaderMath.Hash01(basis ^ 0xC2B2AE35u) - 0.5f) * cellSize;
        var positionY = center.Y + (DecorativeMosaicShaderMath.Hash01(basis * 0x85EBCA6Bu + 0x9E3779B9u) - 0.5f) * cellSize;
        site[index] = new Float4(positionX, positionY, theta[index], sizeFactor);
        siteReference[index] = DecorativeMosaicShaderMath.OwnerReference(positionX, positionY, cellSize, gridWidth, gridHeight);
        siteRank[index] = Hlsl.Min(
            (int)(DecorativeMosaicShaderMath.Hash01(basis + 0x27D4EB2Fu) * DecorativeMosaicSettings.LayingSteps),
            DecorativeMosaicSettings.LayingSteps - 1);
        Hlsl.InterlockedAdd(ref scratch[0], 1);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SiteSeedShader(
    ReadWriteBuffer<int> siteRank,
    ReadWriteBuffer<Float4> site,
    ReadWriteBuffer<int> jumpFlood,
    int gridWidth,
    int gridHeight,
    float cellSize) : IComputeShader
{
    private readonly ReadWriteBuffer<int> siteRank = siteRank;
    private readonly ReadWriteBuffer<Float4> site = site;
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (siteRank[index] < 0)
            return;

        var packed = site[index];
        var cx = Hlsl.Clamp((int)(packed.X / cellSize), 0, gridWidth - 1);
        var cy = Hlsl.Clamp((int)(packed.Y / cellSize), 0, gridHeight - 1);
        Hlsl.InterlockedMin(ref jumpFlood[cy * gridWidth + cx], index);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SiteJumpFloodPassShader(
    ReadWriteBuffer<int> input,
    ReadWriteBuffer<int> output,
    ReadWriteBuffer<Float4> site,
    int gridWidth,
    int gridHeight,
    int stepSize,
    float cellSize,
    float gamma) : IComputeShader
{
    private readonly ReadWriteBuffer<int> input = input;
    private readonly ReadWriteBuffer<int> output = output;
    private readonly ReadWriteBuffer<Float4> site = site;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int stepSize = stepSize;
    private readonly float cellSize = cellSize;
    private readonly float gamma = gamma;

    private float Distance(Float2 position, int candidate)
    {
        var packed = site[candidate];
        var dx = position.X - packed.X;
        var dy = position.Y - packed.Y;
        var angle = packed.Z;
        var c = Hlsl.Cos(angle);
        var s = Hlsl.Sin(angle);
        var u = c * dx + s * dy;
        var v = -s * dx + c * dy;
        return (Hlsl.Abs(u) / gamma + Hlsl.Abs(v) * gamma) / Hlsl.Max(packed.W, 0.1f);
    }

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= gridWidth || y >= gridHeight)
            return;

        var position = DecorativeMosaicShaderMath.CellCenter(x, y, cellSize);
        var best = DecorativeMosaicSettings.SiteSentinel;
        var bestDistance = 3.402823e+38f;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = x + dx * stepSize;
                var sy = y + dy * stepSize;
                if (sx < 0 || sx >= gridWidth || sy < 0 || sy >= gridHeight)
                    continue;
                var candidate = input[sy * gridWidth + sx];
                if (candidate == DecorativeMosaicSettings.SiteSentinel || candidate == best)
                    continue;
                var distance = Distance(position, candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }
        output[y * gridWidth + x] = best;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CentroidAccumulateShader(
    ReadWriteBuffer<int> siteMap,
    ReadWriteBuffer<int> mask,
    ReadWriteBuffer<float> edgeDistance,
    ReadWriteBuffer<Float2> siteReference,
    ReadWriteBuffer<int> sumX,
    ReadWriteBuffer<int> sumY,
    ReadWriteBuffer<int> count,
    int gridWidth,
    int gridHeight,
    float cellSize,
    float avoidWidth,
    float cutoff) : IComputeShader
{
    private readonly ReadWriteBuffer<int> siteMap = siteMap;
    private readonly ReadWriteBuffer<int> mask = mask;
    private readonly ReadWriteBuffer<float> edgeDistance = edgeDistance;
    private readonly ReadWriteBuffer<Float2> siteReference = siteReference;
    private readonly ReadWriteBuffer<int> sumX = sumX;
    private readonly ReadWriteBuffer<int> sumY = sumY;
    private readonly ReadWriteBuffer<int> count = count;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;
    private readonly float avoidWidth = avoidWidth;
    private readonly float cutoff = cutoff;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (mask[index] != 1)
            return;
        if (avoidWidth > 0f && edgeDistance[index] < avoidWidth)
            return;
        var owner = siteMap[index];
        if (owner == DecorativeMosaicSettings.SiteSentinel)
            return;

        var reference = siteReference[owner];
        var center = DecorativeMosaicShaderMath.CellCenter(gx, gy, cellSize);
        var offsetX = center.X - reference.X;
        var offsetY = center.Y - reference.Y;
        if (Hlsl.Abs(offsetX) > cutoff || Hlsl.Abs(offsetY) > cutoff)
            return;

        Hlsl.InterlockedAdd(ref sumX[owner], (int)Hlsl.Round(offsetX * DecorativeMosaicSettings.FixedPointScale));
        Hlsl.InterlockedAdd(ref sumY[owner], (int)Hlsl.Round(offsetY * DecorativeMosaicSettings.FixedPointScale));
        Hlsl.InterlockedAdd(ref count[owner], 1);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SiteMoveShader(
    ReadWriteBuffer<int> siteRank,
    ReadWriteBuffer<Float4> site,
    ReadWriteBuffer<Float2> siteReference,
    ReadWriteBuffer<int> sumX,
    ReadWriteBuffer<int> sumY,
    ReadWriteBuffer<int> count,
    ReadWriteBuffer<float> theta,
    ReadWriteBuffer<float> edgeDistance,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight,
    float cellSize,
    float spacing,
    float detail) : IComputeShader
{
    private readonly ReadWriteBuffer<int> siteRank = siteRank;
    private readonly ReadWriteBuffer<Float4> site = site;
    private readonly ReadWriteBuffer<Float2> siteReference = siteReference;
    private readonly ReadWriteBuffer<int> sumX = sumX;
    private readonly ReadWriteBuffer<int> sumY = sumY;
    private readonly ReadWriteBuffer<int> count = count;
    private readonly ReadWriteBuffer<float> theta = theta;
    private readonly ReadWriteBuffer<float> edgeDistance = edgeDistance;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;
    private readonly float spacing = spacing;
    private readonly float detail = detail;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var cells = count[index];
        var accumulatedX = sumX[index];
        var accumulatedY = sumY[index];
        sumX[index] = 0;
        sumY[index] = 0;
        count[index] = 0;
        if (siteRank[index] < 0)
            return;

        if (cells == 0)
        {
            siteRank[index] = -1;
            Hlsl.InterlockedAdd(ref scratch[0], -1);
            return;
        }

        var reference = siteReference[index];
        var scale = 1f / (DecorativeMosaicSettings.FixedPointScale * cells);
        var newX = Hlsl.Clamp(reference.X + accumulatedX * scale, 0f, gridWidth * cellSize - 0.001f);
        var newY = Hlsl.Clamp(reference.Y + accumulatedY * scale, 0f, gridHeight * cellSize - 0.001f);

        var cellX = Hlsl.Clamp((int)(newX / cellSize), 0, gridWidth - 1);
        var cellY = Hlsl.Clamp((int)(newY / cellSize), 0, gridHeight - 1);
        var cell = cellY * gridWidth + cellX;
        site[index] = new Float4(newX, newY, theta[cell], DecorativeMosaicShaderMath.SizeFactor(edgeDistance[cell], spacing, detail));
        siteReference[index] = DecorativeMosaicShaderMath.OwnerReference(newX, newY, cellSize, gridWidth, gridHeight);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RenderShader(
    ReadWriteBuffer<int> siteMap,
    ReadWriteBuffer<Float4> site,
    ReadWriteBuffer<int> siteRank,
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int rectOffsetX,
    int rectOffsetY,
    int rectWidth,
    int rectHeight,
    int gridWidth,
    int gridHeight,
    int sourceWidth,
    int sourceHeight,
    float cellSize,
    float gamma,
    float halfBase,
    int visibleCount,
    int seed,
    float irregularity,
    float colorVariation,
    float bevel,
    float groutR,
    float groutG,
    float groutB,
    float groutOpacity) : IComputeShader
{
    private readonly ReadWriteBuffer<int> siteMap = siteMap;
    private readonly ReadWriteBuffer<Float4> site = site;
    private readonly ReadWriteBuffer<int> siteRank = siteRank;
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int rectOffsetX = rectOffsetX;
    private readonly int rectOffsetY = rectOffsetY;
    private readonly int rectWidth = rectWidth;
    private readonly int rectHeight = rectHeight;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;
    private readonly float cellSize = cellSize;
    private readonly float gamma = gamma;
    private readonly float halfBase = halfBase;
    private readonly int visibleCount = visibleCount;
    private readonly int seed = seed;
    private readonly float irregularity = irregularity;
    private readonly float colorVariation = colorVariation;
    private readonly float bevel = bevel;
    private readonly float groutR = groutR;
    private readonly float groutG = groutG;
    private readonly float groutB = groutB;
    private readonly float groutOpacity = groutOpacity;

    public void Execute()
    {
        if (ThreadIds.X >= rectWidth || ThreadIds.Y >= rectHeight)
            return;

        var px = ThreadIds.X + rectOffsetX;
        var py = ThreadIds.Y + rectOffsetY;
        var sourcePixel = source[new Int2(px, py)];
        var coverage = sourcePixel.W;
        if (coverage <= DecorativeMosaicSettings.AlphaThreshold)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var pxc = px + 0.5f;
        var pyc = py + 0.5f;
        var cellX = Hlsl.Clamp((int)(pxc / cellSize), 0, gridWidth - 1);
        var cellY = Hlsl.Clamp((int)(pyc / cellSize), 0, gridHeight - 1);

        var best = DecorativeMosaicSettings.SiteSentinel;
        var bestDistance = 3.402823e+38f;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = cellX + dx;
                var sy = cellY + dy;
                if (sx < 0 || sx >= gridWidth || sy < 0 || sy >= gridHeight)
                    continue;
                var candidate = siteMap[sy * gridWidth + sx];
                if (candidate == DecorativeMosaicSettings.SiteSentinel || candidate == best)
                    continue;
                var packed = site[candidate];
                var cdx = pxc - packed.X;
                var cdy = pyc - packed.Y;
                var angle = packed.Z;
                var cc = Hlsl.Cos(angle);
                var cs = Hlsl.Sin(angle);
                var cu = cc * cdx + cs * cdy;
                var cv = -cs * cdx + cc * cdy;
                var distance = (Hlsl.Abs(cu) / gamma + Hlsl.Abs(cv) * gamma) / Hlsl.Max(packed.W, 0.1f);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }

        if (best == DecorativeMosaicSettings.SiteSentinel || siteRank[best] >= visibleCount)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var basis = (uint)best * 0x9E3779B9u ^ (uint)seed * 0xC2B2AE35u;
        var rotation = (DecorativeMosaicShaderMath.Hash01(basis) - 0.5f) * 2f * DecorativeMosaicSettings.MaximumRotationJitter * irregularity;
        var rc = Hlsl.Cos(rotation);
        var rs = Hlsl.Abs(Hlsl.Sin(rotation));
        var shrink = 1f / (rc + rs);
        var bestSite = site[best];
        var sizeJitter = 1f - DecorativeMosaicSettings.SizeJitterFactor * irregularity * DecorativeMosaicShaderMath.Hash01(basis ^ 0x85EBCA6Bu);
        var factor = bestSite.W * shrink * sizeJitter;
        var halfU = gamma * halfBase * factor;
        var halfV = halfBase / gamma * factor;

        var tileAngle = bestSite.Z + rotation;
        var tc2 = Hlsl.Cos(tileAngle);
        var ts2 = Hlsl.Sin(tileAngle);
        var dx2 = pxc - bestSite.X;
        var dy2 = pyc - bestSite.Y;
        var u = tc2 * dx2 + ts2 * dy2;
        var v = -ts2 * dx2 + tc2 * dy2;

        if (Hlsl.Abs(u) <= halfU && Hlsl.Abs(v) <= halfV)
        {
            var tileX = Hlsl.Clamp((int)bestSite.X, 0, sourceWidth - 1);
            var tileY = Hlsl.Clamp((int)bestSite.Y, 0, sourceHeight - 1);
            var tileSample = source[new Int2(tileX, tileY)];
            var tileAlpha = Hlsl.Max(tileSample.W, 1e-4f);
            var r = tileSample.X / tileAlpha;
            var g = tileSample.Y / tileAlpha;
            var b = tileSample.Z / tileAlpha;

            var variation = 1f + (DecorativeMosaicShaderMath.Hash01(basis * 0x85EBCA6Bu + 0x9E3779B9u) - 0.5f) * 2f * DecorativeMosaicSettings.ColorVariationScale * colorVariation;
            var edgeU = halfU - Hlsl.Abs(u);
            var edgeV = halfV - Hlsl.Abs(v);
            var bevelWidth = DecorativeMosaicSettings.BevelWidthFactor * Hlsl.Min(halfU, halfV);
            var rim = Hlsl.Saturate(Hlsl.Min(edgeU, edgeV) / Hlsl.Max(bevelWidth, 1e-4f));
            var normalU = edgeU < edgeV ? (u > 0f ? 1f : -1f) : 0f;
            var normalV = edgeU < edgeV ? 0f : (v > 0f ? 1f : -1f);
            var normalX = normalU * tc2 - normalV * ts2;
            var normalY = normalU * ts2 + normalV * tc2;
            var shade = 1f + bevel * DecorativeMosaicSettings.BevelShadeScale * (1f - rim) * (normalX * -0.5547f + normalY * -0.8321f);
            var tilt = 1f + bevel * DecorativeMosaicSettings.BevelTiltScale * (DecorativeMosaicShaderMath.Hash01(basis + 0x27D4EB2Fu) - 0.5f) * 2f;
            var scale = variation * shade * tilt;
            r = Hlsl.Saturate(r * scale);
            g = Hlsl.Saturate(g * scale);
            b = Hlsl.Saturate(b * scale);
            output[ThreadIds.XY] = new Float4(r * coverage, g * coverage, b * coverage, coverage);
            return;
        }

        var groutAlpha = groutOpacity * coverage;
        output[ThreadIds.XY] = new Float4(groutR * groutAlpha, groutG * groutAlpha, groutB * groutAlpha, groutAlpha);
    }
}

internal static class DecorativeMosaicShaderMath
{
    public static Float2 CellCenter(int i, int j, float cellSize)
        => new((i + 0.5f) * cellSize, (j + 0.5f) * cellSize);

    public static Float2 OwnerReference(float x, float y, float cellSize, int gridWidth, int gridHeight)
        => CellCenter(
            Hlsl.Clamp((int)(x / cellSize), 0, gridWidth - 1),
            Hlsl.Clamp((int)(y / cellSize), 0, gridHeight - 1),
            cellSize);

    public static float SizeFactor(float edgeDistance, float spacing, float detail)
        => 1f - detail * (1f - DecorativeMosaicSettings.MinimumSizeFactor)
            * Hlsl.Saturate(1f - edgeDistance / (DecorativeMosaicSettings.DetailRangeFactor * spacing));

    public static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value * 2.3283064e-10f;
    }
}
