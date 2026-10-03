using System.Reflection;
using System.Text.Json;

namespace TriconexToTia.Mapper;

/// <summary>Loads mapping rules from an external file or the embedded default.</summary>
public static class MappingLoader
{
    private const string ResourceName = "TriconexToTia.Mapper.mappings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Mappings Load(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Custom mappings file not found: {path}", path);
            }
            var custom = JsonSerializer.Deserialize<Mappings>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException($"Mappings file {path} could not be parsed.");
            custom.Source = path;
            return custom.Normalize();
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                "Embedded mappings.json is missing. Ensure it is embedded in TriconexToTia.Mapper.");
        var embedded = JsonSerializer.Deserialize<Mappings>(stream, Options)
            ?? throw new InvalidDataException("Embedded mappings.json could not be parsed.");
        embedded.Source = "embedded";
        return embedded.Normalize();
    }
}
