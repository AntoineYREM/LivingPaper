using LivingPaper.Application;

namespace LivingPaper.Application.Tests;

// The index of the passages, in memory. Search is not used by the code under test.
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
        _books[book] = passages;
        return Task.CompletedTask;
    }

    public Task SaveVectorsAsync(BookKey book, string embeddingModel, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PassageIndexState?> GetStateAsync(BookKey book, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<StoredPassage>> GetPassagesAsync(BookKey book, CancellationToken cancellationToken) =>
        Task.FromResult(_books.GetValueOrDefault(book, []));

    public Task<IReadOnlyList<FoundPassage>> SearchAsync(BookKey book, float[] query, int count, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task DeleteAsync(BookKey book, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Deleted.Add(book);
        _books.Remove(book);
        return Task.CompletedTask;
    }
}
