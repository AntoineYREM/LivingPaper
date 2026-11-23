using System.Text;
using System.Text.Json;

namespace LivingPaper.Evals;

// Keeps the runs as JSON files, one per run. The folder is not part of what is committed (evals/runs/ is ignored by git): a run holds
// the text of the replies. A run worth sharing is copied by hand to evals/published/.
internal static class RunStore
{
    public const long MaxFileBytes = 20 * 1_048_576;

    private const int MaxModelSlugLength = 40;

    // Accents and apostrophes are written as they are, not as unicode escapes: the file is read by people, and the replies are often in
    // French. Relaxed escaping is fine here because the file is never put in a web page.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The time (UTC) and the model: sorted by name, they are sorted by date, and the model is readable in a list of files.
    public static string NewId(DateTimeOffset startedAt, string chatModel) =>
        $"{startedAt.UtcDateTime:yyyyMMdd-HHmmss}-{Slug(chatModel)}";

    // Never overwrites a file: a run with the same id gets "-2", "-3"... at the end of its name.
    public static async Task<string> SaveAsync(string folder, RunRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        Directory.CreateDirectory(folder);
        var json = JsonSerializer.Serialize(record, Options);
        for (var attempt = 1; ; attempt++)
        {
            var path = Path.Combine(folder, attempt == 1 ? $"{record.Id}.json" : $"{record.Id}-{attempt}.json");
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(false);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Taken: try the next name.
            }
        }
    }

    public static async Task<RunRecord> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            throw new EvalException($"The run file {name} does not exist.");
        }

        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new EvalException($"The run file {name} is too large.");
        }

        RunRecord? record;
        try
        {
            await using var stream = File.OpenRead(path);
            record = await JsonSerializer.DeserializeAsync<RunRecord>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new EvalException($"The run file {name} is not valid JSON.");
        }

        if (record is null || record.SchemaVersion != RunRecord.CurrentSchemaVersion)
        {
            throw new EvalException($"The run file {name} must have \"schemaVersion\": {RunRecord.CurrentSchemaVersion}.");
        }

        // A file edited by hand can leave a part out: the missing part is named at once, not found later as a crash.
        if (string.IsNullOrWhiteSpace(record.Id) || record.Chat is null || record.Judge is null || record.Tokens is null || record.Results is null)
        {
            throw new EvalException($"The run file {name} is incomplete.");
        }

        return record;
    }

    // Lowercase letters and digits, the rest a single hyphen: safe in a file name, whatever the name of the model ("Qwen/Qwen3:32b").
    private static string Slug(string model)
    {
        var slug = new StringBuilder();
        foreach (var character in model.ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(character);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var text = slug.ToString().Trim('-');
        if (text.Length > MaxModelSlugLength)
        {
            text = text[..MaxModelSlugLength].Trim('-');
        }

        return text.Length == 0 ? "model" : text;
    }
}
