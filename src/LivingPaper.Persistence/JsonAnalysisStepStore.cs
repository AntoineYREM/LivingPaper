using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The intermediate results of an analysis, in {booksDirectory}/{id}.steps.json, next to the book's own file.
public sealed class JsonAnalysisStepStore(string booksDirectory) : IAnalysisStepStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private const int MaxKeyLength = 100;

    // The analysis saves one step at a time, but a step is read, changed and written back: this keeps two saves
    // from overwriting each other.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task SaveAsync(BookId bookId, string key, string result, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(result);
        if (key.Length > MaxKeyLength)
        {
            throw new ArgumentException($"A step key is at most {MaxKeyLength} characters.", nameof(key));
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var steps = new Dictionary<string, string>(await GetAllAsync(bookId, cancellationToken).ConfigureAwait(false))
            {
                [key] = result,
            };
            var content = JsonSerializer.SerializeToUtf8Bytes(
                new StepsFile { SchemaVersion = CurrentSchemaVersion, Steps = steps },
                BookFile.JsonOptions);
            await AtomicFile.WriteAsync(PathOf(bookId), content, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(BookId bookId, CancellationToken cancellationToken)
    {
        var path = PathOf(bookId);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var bytes = await SharedFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<StepsFile>(bytes, BookFile.JsonOptions);
            return file is { SchemaVersion: CurrentSchemaVersion, Steps: not null }
                ? file.Steps
                : new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // Steps are only a way to avoid paying twice: unreadable ones mean the analysis starts over.
            return new Dictionary<string, string>();
        }
    }

    public Task DeleteAllAsync(BookId bookId, CancellationToken cancellationToken) =>
        SharedFile.DeleteIfExistsAsync(PathOf(bookId), cancellationToken);

    public void Dispose() => _writeLock.Dispose();

    private string PathOf(BookId bookId) => Path.Combine(booksDirectory, bookId + ".steps.json");

    private sealed class StepsFile
    {
        public int SchemaVersion { get; set; }

        public Dictionary<string, string>? Steps { get; set; }
    }
}
