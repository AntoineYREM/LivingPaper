using System.Text.Json;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The language models in {dataDirectory}/settings.json, with the keys encrypted by the ISecretProtector. This file is
// never part of a book export. An existing file that cannot be used is an explicit error, never replaced by empty
// settings: that would silently throw away the user's models and keys.
public sealed partial class JsonLlmSettingsRepository(string dataDirectory, ISecretProtector protector) : ILlmSettingsRepository
{
    public const string FileName = "settings.json";

    // Twenty models with long names are a few kilobytes: anything this big is not our file.
    public const int MaxFileBytes = 1_048_576;

    private string FilePath => Path.Combine(dataDirectory, FileName);

    public async Task<LlmSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var path = FilePath;
        if (!File.Exists(path))
        {
            return new LlmSettings();
        }

        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new SettingsFileException(FileName, "the file is too large");
        }

        var file = await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        var providers = new List<LlmProvider>();
        var unreadable = new List<string>();
        var entries = file.Providers ?? [];
        for (var index = 0; index < entries.Count; index++)
        {
            providers.Add(ToProvider(entries[index], index + 1, unreadable));
        }

        try
        {
            return LlmSettings.Restore(providers, file.AnalysisProviderId, file.ChatProviderId, unreadable);
        }
        catch (ArgumentException exception)
        {
            // Its message says "two models have the same id/name" or "too many models": no value from the file.
            throw new SettingsFileException(FileName, exception.Message.Replace(" (Parameter 'providers')", string.Empty, StringComparison.Ordinal));
        }
    }

    public async Task SaveAsync(LlmSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // A model whose key could not be decrypted keeps the text it had in the file: writing it back as "no key" would
        // destroy it on the first unrelated change, and the user could not tell it apart from a model with no key.
        var kept = settings.UnreadableKeyProviderIds.Count > 0
            ? await ReadStoredKeysAsync(cancellationToken).ConfigureAwait(false)
            : [];

        var file = new SettingsFile
        {
            SchemaVersion = SettingsFile.CurrentSchemaVersion,
            Providers =
            [
                .. settings.Providers.Select(provider => new ProviderEntry
                {
                    Id = provider.Id,
                    Name = provider.Name,
                    BaseUrl = provider.BaseUrl,
                    Model = provider.Model,
                    ProtectedApiKey = ProtectedKeyOf(provider, settings, kept),
                }),
            ],
            AnalysisProviderId = settings.AnalysisProviderId,
            ChatProviderId = settings.ChatProviderId,
        };

        await AtomicFile.WriteAsync(FilePath, JsonSerializer.SerializeToUtf8Bytes(file, SettingsFile.JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private string? ProtectedKeyOf(LlmProvider provider, LlmSettings settings, Dictionary<string, string> kept)
    {
        if (settings.UnreadableKeyProviderIds.Contains(provider.Id))
        {
            return kept.GetValueOrDefault(provider.Id);
        }

        return provider.ApiKey.Length == 0 ? null : protector.Protect(provider.ApiKey);
    }

    private LlmProvider ToProvider(ProviderEntry entry, int position, List<string> unreadable)
    {
        if (entry is null || !IdPattern().IsMatch(entry.Id ?? string.Empty))
        {
            throw new SettingsFileException(FileName, $"model {position}: the id is missing or is not valid");
        }

        // Same rules as when the model was added. The errors name the field and the code, never what is in the file.
        var errors = LlmProviderValidator.Validate(new LlmProviderDraft(entry.Name, entry.BaseUrl, string.Empty, entry.Model), []);
        if (errors.Count > 0)
        {
            throw new SettingsFileException(FileName, $"model {position}: {string.Join(", ", errors.Select(error => $"{error.Field} ({error.Code})"))}");
        }

        var key = string.Empty;
        if (!string.IsNullOrEmpty(entry.ProtectedApiKey) && !protector.TryUnprotect(entry.ProtectedApiKey, out key))
        {
            unreadable.Add(entry.Id!);
            key = string.Empty;
        }

        return new LlmProvider(entry.Id!, entry.Name!.Trim(), entry.BaseUrl!.Trim(), key, entry.Model!.Trim());
    }

    private static async Task<SettingsFile> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        SettingsFile? file;
        try
        {
            var bytes = await SharedFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            file = JsonSerializer.Deserialize<SettingsFile>(bytes, SettingsFile.JsonOptions);
        }
        catch (JsonException exception)
        {
            // Where it broke ("line 12, position 3"), never what the file contains: it holds encrypted keys.
            throw new SettingsFileException(FileName, $"the file is not valid JSON ({exception.GetType().Name}, line {exception.LineNumber}, position {exception.BytePositionInLine})");
        }

        if (file is null)
        {
            throw new SettingsFileException(FileName, "the file is empty");
        }

        if (file.SchemaVersion != SettingsFile.CurrentSchemaVersion)
        {
            throw new SettingsFileException(FileName, $"unsupported schemaVersion {file.SchemaVersion} (expected {SettingsFile.CurrentSchemaVersion})");
        }

        return file;
    }

    // The encrypted keys of the file as it is now, by model id. Empty when the file cannot be read.
    private async Task<Dictionary<string, string>> ReadStoredKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            var file = await ReadFileAsync(FilePath, cancellationToken).ConfigureAwait(false);
            return (file.Providers ?? [])
                .Where(entry => entry?.Id is not null && !string.IsNullOrEmpty(entry.ProtectedApiKey))
                .GroupBy(entry => entry.Id!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().ProtectedApiKey!, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is SettingsFileException or FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
    }

    // The ids the application makes (32 lower-case hexadecimal characters): they appear in forms and URLs.
    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
