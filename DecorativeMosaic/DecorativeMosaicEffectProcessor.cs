using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace DecorativeMosaic;

internal sealed class DecorativeMosaicEffectProcessor : VideoEffectProcessorBase
{
    private readonly IGraphicsDevicesAndContext _devices;
    private readonly DecorativeMosaicEffect _item;
    private DecorativeMosaicGpuInterop? _interop;
    private DecorativeMosaicPipeline? _pipeline;
    private DecorativeMosaicCustomEffect? _effect;
    private Crop? _outputCrop;
    private ID2D1Image? _outputCropOutput;
    private AffineTransform2D? _outputTransform;
    private ID2D1Image? _outputTransformOutput;
    private bool _isFirst = true;
    private bool _hasOutput;
    private bool _hasOutputOffset;
    private bool _hasCropRect;
    private bool _hasRenderState;
    private Vector2 _outputOffset;
    private Vector4 _cropRect;
    private Parameters _parameters;
    private RenderState _renderState;

    public DecorativeMosaicEffectProcessor(IGraphicsDevicesAndContext devices, DecorativeMosaicEffect item)
        : base(devices)
    {
        _devices = devices;
        _item = item;
    }

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || _effect is null || _outputCrop is null || _outputTransform is null || _outputTransformOutput is null || _interop is null || _pipeline is null || input is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var parameters = new Parameters(
            (float)(_item.Amount.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Laying.GetValue(frame, length, fps) / 100.0),
            _item.Quality,
            (float)_item.TileSize.GetValue(frame, length, fps),
            (float)(_item.Grout.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Aspect.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Irregularity.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Detail.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Align.GetValue(frame, length, fps) / 100.0),
            (float)(_item.EdgeDetect.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Bevel.GetValue(frame, length, fps) / 100.0),
            (float)(_item.ColorVariation.GetValue(frame, length, fps) / 100.0),
            _item.GroutColor,
            (float)(_item.GroutOpacity.GetValue(frame, length, fps) / 100.0),
            _item.Seed);

        if (_isFirst || _parameters.Amount != parameters.Amount)
            _effect.Amount = parameters.Amount;

        if (parameters.Amount <= 0f || parameters.Laying <= 0f)
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var bounds = _devices.DeviceContext.GetImageLocalBounds(input);
        var widthValue = Math.Ceiling((double)bounds.Right - bounds.Left);
        var heightValue = Math.Ceiling((double)bounds.Bottom - bounds.Top);
        if (!double.IsFinite(widthValue) || !double.IsFinite(heightValue) ||
            !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            widthValue <= 0d || heightValue <= 0d ||
            widthValue > DecorativeMosaicSettings.MaximumCanvasSize ||
            heightValue > DecorativeMosaicSettings.MaximumCanvasSize)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var canvasWidth = (int)widthValue;
        var canvasHeight = (int)heightValue;

        _interop.EnsureSource(canvasWidth, canvasHeight);
        _interop.RenderInput(input, new Vortice.RawRectF(bounds.Left, bounds.Top, bounds.Left + canvasWidth, bounds.Top + canvasHeight));

        var pipelineParameters = new DecorativeMosaicPipeline.Parameters(
            parameters.Quality,
            Math.Clamp(parameters.Laying, 0f, 1f),
            Math.Clamp(parameters.TileSize, DecorativeMosaicSettings.MinimumTileSize, DecorativeMosaicSettings.MaximumTileSize),
            Math.Clamp(parameters.Grout, 0f, 1f),
            Math.Clamp(parameters.Aspect, 0f, 1f),
            Math.Clamp(parameters.Align, 0f, 1f),
            Math.Clamp(parameters.EdgeDetect, 0f, 1f),
            Math.Clamp(parameters.Detail, 0f, 1f),
            Math.Clamp(parameters.Irregularity, 0f, 1f),
            Math.Clamp(parameters.ColorVariation, 0f, 1f),
            Math.Clamp(parameters.Bevel, 0f, 1f),
            parameters.GroutColor.R / 255f,
            parameters.GroutColor.G / 255f,
            parameters.GroutColor.B / 255f,
            Math.Clamp(parameters.GroutOpacity, 0f, 1f),
            Math.Max(parameters.Seed, 0));

        bool structureChanged;
        _interop.BeginCompute();
        try
        {
            structureChanged = _pipeline.Simulate(
                _interop.SourceTexture,
                canvasWidth,
                canvasHeight,
                in pipelineParameters);
        }
        finally
        {
            _interop.EndCompute();
        }

        if (!_pipeline.TryGetVisibleBounds(canvasWidth, canvasHeight, in pipelineParameters, out var rect))
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            _hasRenderState = false;
            return effectDescription.DrawDescription;
        }

        if (!_interop.OutputCovers(rect.Width, rect.Height))
            _outputCrop.SetInput(0, null, true);
        var outputChanged = _interop.EnsureOutput(rect.Width, rect.Height);
        var renderState = new RenderState(
            pipelineParameters.Laying,
            pipelineParameters.Grout,
            pipelineParameters.Irregularity,
            pipelineParameters.ColorVariation,
            pipelineParameters.Bevel,
            pipelineParameters.GroutR,
            pipelineParameters.GroutG,
            pipelineParameters.GroutB,
            pipelineParameters.GroutOpacity,
            rect);
        if (structureChanged || outputChanged || !_hasOutput || !_hasRenderState || _renderState != renderState)
        {
            _interop.BeginCompute();
            try
            {
                _pipeline.RenderVisible(
                    _interop.SourceTexture,
                    _interop.OutputTexture,
                    canvasWidth,
                    canvasHeight,
                    rect,
                    in pipelineParameters);
            }
            finally
            {
                _interop.EndCompute();
            }
            _renderState = renderState;
            _hasRenderState = true;
        }

        if (outputChanged || !_hasOutput)
        {
            _outputCrop.SetInput(0, _interop.OutputBitmap, true);
            _effect.SetInput(1, _outputTransformOutput, true);
        }
        var cropRect = new Vector4(0f, 0f, rect.Width, rect.Height);
        if (!_hasCropRect || _cropRect != cropRect)
        {
            _outputCrop.Rectangle = cropRect;
            _cropRect = cropRect;
            _hasCropRect = true;
        }
        var outputOffset = new Vector2(bounds.Left + rect.X, bounds.Top + rect.Y);
        if (!_hasOutputOffset || _outputOffset != outputOffset)
        {
            _outputTransform.TransformMatrix = Matrix3x2.CreateTranslation(outputOffset);
            _outputOffset = outputOffset;
            _hasOutputOffset = true;
        }
        _hasOutput = true;
        _parameters = parameters;
        _isFirst = false;
        return effectDescription.DrawDescription;
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        var interop = DecorativeMosaicGpuInterop.TryCreate(devices);
        if (interop is null)
            return null;
        var pipeline = DecorativeMosaicPipeline.TryCreate(interop.Device);
        if (pipeline is null)
        {
            interop.Dispose();
            return null;
        }

        DecorativeMosaicCustomEffect? effect = null;
        Crop? outputCrop = null;
        ID2D1Image? outputCropOutput = null;
        AffineTransform2D? outputTransform = null;
        ID2D1Image? outputTransformOutput = null;
        ID2D1Image? output = null;
        try
        {
            effect = new DecorativeMosaicCustomEffect(devices);
            if (!effect.IsEnabled)
            {
                effect.Dispose();
                pipeline.Dispose();
                interop.Dispose();
                return null;
            }
            outputCrop = new Crop(devices.DeviceContext);
            outputCropOutput = outputCrop.Output;
            outputTransform = new AffineTransform2D(devices.DeviceContext)
            {
                BorderMode = BorderMode.Hard,
            };
            outputTransform.SetInput(0, outputCropOutput, true);
            outputTransformOutput = outputTransform.Output;
            output = effect.Output;
            _interop = interop;
            _pipeline = pipeline;
            _effect = effect;
            _outputCrop = outputCrop;
            _outputCropOutput = outputCropOutput;
            _outputTransform = outputTransform;
            _outputTransformOutput = outputTransformOutput;
            disposer.Collect(effect);
            disposer.Collect(outputCrop);
            disposer.Collect(outputCropOutput);
            disposer.Collect(outputTransform);
            disposer.Collect(outputTransformOutput);
            disposer.Collect(output);
            return output;
        }
        catch
        {
            output?.Dispose();
            outputTransformOutput?.Dispose();
            outputTransform?.Dispose();
            outputCropOutput?.Dispose();
            outputCrop?.Dispose();
            effect?.Dispose();
            pipeline.Dispose();
            interop.Dispose();
            throw;
        }
    }

    protected override void setInput(ID2D1Image? inputImage)
    {
        _effect?.SetInput(0, inputImage, true);
        if (!_hasOutput)
            _effect?.SetInput(1, inputImage, true);
    }

    protected override void ClearEffectChain()
    {
        _effect?.SetInput(0, null, true);
        _effect?.SetInput(1, null, true);
        _outputCrop?.SetInput(0, null, true);
        _isFirst = true;
        _hasOutput = false;
        _hasOutputOffset = false;
        _hasCropRect = false;
        _hasRenderState = false;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                ClearEffectChain();
                _interop?.WaitForIdle();
                _pipeline?.Dispose();
                _pipeline = null;
                _interop?.Dispose();
                _interop = null;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    private readonly record struct RenderState(
        float Laying,
        float Grout,
        float Irregularity,
        float ColorVariation,
        float Bevel,
        float GroutR,
        float GroutG,
        float GroutB,
        float GroutOpacity,
        DecorativeMosaicPipeline.PixelRect Rect);

    private readonly record struct Parameters(
        float Amount,
        float Laying,
        DecorativeMosaicQuality Quality,
        float TileSize,
        float Grout,
        float Aspect,
        float Irregularity,
        float Detail,
        float Align,
        float EdgeDetect,
        float Bevel,
        float ColorVariation,
        System.Windows.Media.Color GroutColor,
        float GroutOpacity,
        int Seed);
}
