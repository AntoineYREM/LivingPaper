using System.Text.Json;
using LivingPaper.Application;

namespace LivingPaper.Persistence;

// Called when the application starts. Copies the whole default prompts file to data/prompts.json the first time, and
// after an update adds the prompts that the file does not have yet. A prompt the user edited is never changed. A prompt that is
// still exactly an earlier default (see ReplacedDefaults) was never edited: it is replaced by the new default, so that an improvement
// of a prompt reaches everyone. To go back to the defaults, the user deletes the file.
public static class PromptFileSynchronizer
{
    // Throws PromptFileException when an existing file cannot be read: it is left untouched, so the user can fix it.
    public static async Task EnsureAsync(string dataDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FilePromptProvider.FileName);
        var defaults = DefaultPrompts.Load();

        if (!File.Exists(path))
        {
            await WriteAsync(path, defaults, cancellationToken).ConfigureAwait(false);
            return;
        }

        var existing = await PromptFile.ReadAsync(path, FilePromptProvider.FileName, cancellationToken).ConfigureAwait(false);
        var added = false;
        foreach (var (key, entry) in defaults.Prompts!)
        {
            if (!existing.Prompts!.TryGetValue(key, out var current))
            {
                existing.Prompts[key] = entry;
                added = true;
            }
            else if (current?.Template is { } template && template != entry.Template && ReplacedDefaults.WasShipped(key, template))
            {
                existing.Prompts[key] = entry;
                added = true;
            }
        }

        if (added)
        {
            await WriteAsync(path, existing, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task WriteAsync(string path, PromptFile file, CancellationToken cancellationToken) =>
        AtomicFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(file, PromptFile.JsonOptions), cancellationToken);
}
