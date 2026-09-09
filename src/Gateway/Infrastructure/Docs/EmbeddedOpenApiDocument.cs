using System.Reflection;

namespace OrderToCash.Gateway.Infrastructure.Docs;

/// <summary>
/// Reads the copied <c>specs/shared/openapi.yaml</c> bytes embedded into
/// this assembly at build time (<c>Gateway.csproj</c>'s
/// <c>EmbeddedResource</c>) — never a filesystem path, which may not exist
/// inside a deployed container. Read ONCE and cached: the file does not
/// change at runtime.
/// </summary>
public static class EmbeddedOpenApiDocument
{
    private static readonly Lazy<string> _yaml = new(ReadYaml);

    public static string Yaml => _yaml.Value;

    private static string ReadYaml()
    {
        var assembly = typeof(EmbeddedOpenApiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream("openapi.yaml")
            ?? throw new InvalidOperationException("The embedded openapi.yaml resource was not found in the Gateway assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
