using LivingPaper.Application;

namespace LivingPaper.Application.Tests;

// The index of the passages, in memory. A search answers what the test says.
internal sealed class FakePassageIndex : IPassageIndex
{
    private readonly Dictionary<BookKey, IReadOnlyList<StoredPassage>> _books = [];

    public PassageIndexProblem? Problem { get; set; }

    // Thrown by every save and delete when set.
    public PassageIndexException? Failure { get; set; }

    public List<BookKey> Deleted { get; } = [];

    // Called before passages are saved: what the rest of the application looks like at that moment.
    public Action? OnSave { get; set; }

    public IReadOnlyDictionary<BookKey, IReadOnlyList<StoredPassage>> Books => _books;

    public PassageIndexProblem? Check() => Problem;

    public Task SavePassagesAsync(BookKey book, IReadOnlyList<StoredPassage> passages, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        OnSave?.Invoke();
        Vectors.Remove(book);
        Unchecked.Remove(book);
        _books[book] = passages;
        return Task.CompletedTask;
    }

    // The model and the vectors of the books that have some.
    public Dictionary<BookKey, (string Model, IReadOnlyList<float[]> Vectors)> Vectors { get; } = [];

    public Task SaveVectorsAsync(BookKey book, string embeddingModel, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        if (vectors.Count != _books.GetValueOrDefault(book, []).Count || vectors.Any(vector => vector.Length != vectors[0].Length))
        {
            return Task.FromException(new ArgumentException("The vectors do not match the passages."));
        }

        Vectors[book] = (embeddingModel, vectors);
        Unchecked.Remove(book);
        return Task.CompletedTask;
    }

    // The books whose vectors came with a file and wait to be checked.
    public HashSet<BookKey> Unchecked { get; } = [];

    public async Task SaveUncheckedVectorsAsync(BookKey book, string embeddingModel, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
    {
        await SaveVectorsAsync(book, embeddingModel, vectors, cancellationToken);
        Unchecked.Add(book);
    }

    public Task MarkCheckedAsync(BookKey book, CancellationToken cancellationToken)
    {
        Unchecked.Remove(book);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<float[]>> GetVectorsAsync(BookKey book, CancellationToken cancellationToken) =>
        Failure is not null
            ? Task.FromException<IReadOnlyList<float[]>>(Failure)
            : Task.FromResult(Vectors.TryGetValue(book, out var kept) ? kept.Vectors : []);

    public Task<PassageIndexState?> GetStateAsync(BookKey book, CancellationToken cancellationToken)
    {
        if (!_books.TryGetValue(book, out var passages) || passages.Count == 0)
        {
            return Task.FromResult<PassageIndexState?>(null);
        }

        return Task.FromResult<PassageIndexState?>(Vectors.TryGetValue(book, out var kept)
            ? new PassageIndexState(passages.Count, kept.Model, kept.Vectors[0].Length, Unchecked.Contains(book))
            : new PassageIndexState(passages.Count, null, 0));
    }

    public Task<IReadOnlyList<StoredPassage>> GetPassagesAsync(BookKey book, CancellationToken cancellationToken) =>
        Task.FromResult(_books.GetValueOrDefault(book, []));

    public Func<float[], int, IReadOnlyList<FoundPassage>> Search { get; set; } = (_, _) => [];

    public Task<IReadOnlyList<FoundPassage>> SearchAsync(BookKey book, float[] query, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Search(query, count));

    public Task DeleteAsync(BookKey book, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Deleted.Add(book);
        Vectors.Remove(book);
        Unchecked.Remove(book);
        _books.Remove(book);
        return Task.CompletedTask;
    }
}
