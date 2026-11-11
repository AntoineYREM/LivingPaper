using System.Text.Json;
using LivingPaper.Application;

namespace LivingPaper.Persistence;

// Called when the application starts. Copies the whole default prompts file to data/prompts.json the first time, and
// after an update adds the prompts that the file does not have yet. A prompt already in the file is never changed,
// whether the user edited it or not. To go back to the defaults, the user deletes the file.
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
            if (!existing.Prompts!.ContainsKey(key))
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
