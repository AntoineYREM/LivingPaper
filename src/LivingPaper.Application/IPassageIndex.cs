namespace LivingPaper.Application;

// The passages of the books and their vectors, searched by closeness in meaning (docs/specs/rag.md, section 6). The book
// files stay the source of truth for everything else. Faked in the tests of the layers above.
// Every method throws PassageIndexException when the index cannot be used; its message holds no text of a book.
public interface IPassageIndex
{
    // Null when the index can be used here. Otherwise the feature is off, and the chat uses the story of the book.
    PassageIndexProblem? Check();

    // Replaces the passages of the book. They have no vectors until SaveVectorsAsync: the old ones are removed.
    Task SavePassagesAsync(BookKey book, IReadOnlyList<StoredPassage> passages, CancellationToken cancellationToken);

    // One vector for each passage, in the order of their numbers, all of the same size, with the name of the model that
    // made them. Replaces earlier vectors. Throws ArgumentException when they do not match the passages.
    Task SaveVectorsAsync(BookKey book, string embeddingModel, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken);

    // Null when the book has no passages.
    Task<PassageIndexState?> GetStateAsync(BookKey book, CancellationToken cancellationToken);

    // In the order of their numbers; empty when the book has none.
    Task<IReadOnlyList<StoredPassage>> GetPassagesAsync(BookKey book, CancellationToken cancellationToken);

    // The "count" passages of this book closest to the vector, the closest first; empty when the book has no vectors. The
    // vector must come from the model that made those of the book (see GetStateAsync) and have their size.
    Task<IReadOnlyList<FoundPassage>> SearchAsync(BookKey book, float[] query, int count, CancellationToken cancellationToken);

    // Removes the passages and the vectors of the book; nothing happens when it has none.
    Task DeleteAsync(BookKey book, CancellationToken cancellationToken);
}
