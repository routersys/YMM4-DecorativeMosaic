namespace DecorativeMosaic;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/DecorativeMosaic;component/Shaders/{shaderName}.cso", UriKind.Absolute);
}
