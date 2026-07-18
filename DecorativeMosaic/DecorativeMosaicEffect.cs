using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace DecorativeMosaic;

[VideoEffect(nameof(Texts.DecorativeMosaic), [VideoEffectCategories.Decoration, VideoEffectCategories.Animation], [nameof(Texts.TagMosaic), nameof(Texts.TagTile), nameof(Texts.TagRoman)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class DecorativeMosaicEffect : VideoEffectBase
{
    public override string Label => Texts.DecorativeMosaic;

    public DecorativeMosaicEffect()
    {
        DecorativeMosaicUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Laying), Description = nameof(Texts.LayingDescription), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Laying { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 2, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public DecorativeMosaicQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private DecorativeMosaicQuality _quality = DecorativeMosaicQuality.High;

    [Display(GroupName = nameof(Texts.TileGroup), Name = nameof(Texts.TileSize), Description = nameof(Texts.TileSizeDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "px", 4, 60)]
    public Animation TileSize { get; } = new Animation(14, 2, 200);

    [Display(GroupName = nameof(Texts.TileGroup), Name = nameof(Texts.Grout), Description = nameof(Texts.GroutDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Grout { get; } = new Animation(30, 0, 100);

    [Display(GroupName = nameof(Texts.TileGroup), Name = nameof(Texts.Aspect), Description = nameof(Texts.AspectDescription), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Aspect { get; } = new Animation(0, 0, 100);

    [Display(GroupName = nameof(Texts.TileGroup), Name = nameof(Texts.Irregularity), Description = nameof(Texts.IrregularityDescription), Order = 13, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Irregularity { get; } = new Animation(25, 0, 100);

    [Display(GroupName = nameof(Texts.TileGroup), Name = nameof(Texts.Detail), Description = nameof(Texts.DetailDescription), Order = 14, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Detail { get; } = new Animation(0, 0, 100);

    [Display(GroupName = nameof(Texts.TileGroup), Name = nameof(Texts.Seed), Description = nameof(Texts.SeedDescription), Order = 15, ResourceType = typeof(Texts))]
    [Range(0, int.MaxValue)]
    [DefaultValue(0)]
    [TextBoxSlider("F0", "", 0, 10000)]
    public int Seed
    {
        get => _seed;
        set => Set(ref _seed, Math.Max(value, 0));
    }
    private int _seed;

    [Display(GroupName = nameof(Texts.FlowGroup), Name = nameof(Texts.Align), Description = nameof(Texts.AlignDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Align { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.FlowGroup), Name = nameof(Texts.EdgeDetect), Description = nameof(Texts.EdgeDetectDescription), Order = 21, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation EdgeDetect { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.Bevel), Description = nameof(Texts.BevelDescription), Order = 30, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Bevel { get; } = new Animation(35, 0, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.ColorVariation), Description = nameof(Texts.ColorVariationDescription), Order = 31, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation ColorVariation { get; } = new Animation(20, 0, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.GroutColor), Description = nameof(Texts.GroutColorDescription), Order = 32, ResourceType = typeof(Texts))]
    [ColorPicker]
    public Color GroutColor
    {
        get => _groutColor;
        set => Set(ref _groutColor, value);
    }
    private Color _groutColor = Color.FromArgb(255, 209, 200, 186);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.GroutOpacity), Description = nameof(Texts.GroutOpacityDescription), Order = 33, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation GroutOpacity { get; } = new Animation(100, 0, 100);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        => new DecorativeMosaicEffectProcessor(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, Laying, TileSize, Grout, Aspect, Irregularity, Detail, Align, EdgeDetect, Bevel, ColorVariation, GroutOpacity];
}
