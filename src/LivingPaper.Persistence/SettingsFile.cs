using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LivingPaper.Persistence;

// The shape of data/settings.json: the user's language models and which ones are chosen. The keys are only ever there
// encrypted (protectedApiKey). Kept apart from the Domain so the format can change with its schemaVersion.
internal sealed class SettingsFile
{
    public const int CurrentSchemaVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public int SchemaVersion { get; set; }

    public List<ProviderEntry>? Providers { get; set; }

    public string? AnalysisProviderId { get; set; }

    public string? ChatProviderId { get; set; }
}

internal sealed class ProviderEntry
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? BaseUrl { get; set; }

    public string? Model { get; set; }

    // Never the key itself. Absent when the model has no key.
    public string? ProtectedApiKey { get; set; }
}
