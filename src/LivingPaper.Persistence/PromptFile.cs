using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LivingPaper.Application;

namespace LivingPaper.Persistence;

// The shape of data/prompts.json. Users edit this file by hand, so everything read from it is validated and every
// problem is reported with the file name and the key, never with a prompt value.
internal sealed class PromptFile
{
    public const int CurrentSchemaVersion = 1;

    // Relaxed escaping keeps apostrophes, < and > readable in the file ("don't", not "don't").
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public int SchemaVersion { get; set; }

    // Keyed by prompt key ("chat.system"). Keys this version does not know are kept, never dropped.
    public Dictionary<string, PromptEntry>? Prompts { get; set; }

    public static async Task<PromptFile> ReadAsync(string path, string fileName, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new PromptFileException(fileName, null, "the file was not found (it is created when the application starts)");
        }

        PromptFile? file;
        try
        {
            var bytes = await SharedFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            file = JsonSerializer.Deserialize<PromptFile>(bytes, JsonOptions);
        }
        catch (JsonException exception)
        {
            // JsonException says where it broke ("line 12, position 3"), never what the file contains.
            throw new PromptFileException(fileName, null, $"the file is not valid JSON ({exception.GetType().Name}, line {exception.LineNumber}, position {exception.BytePositionInLine})");
        }

        if (file is null || file.Prompts is null)
        {
            throw new PromptFileException(fileName, null, "the file must contain a \"prompts\" object");
        }

        if (file.SchemaVersion != CurrentSchemaVersion)
        {
            throw new PromptFileException(fileName, null, $"unsupported schemaVersion {file.SchemaVersion} (expected {CurrentSchemaVersion})");
        }

        return file;
    }
}

internal sealed class PromptEntry
{
    public string? Description { get; set; }

    public string? Template { get; set; }
}
