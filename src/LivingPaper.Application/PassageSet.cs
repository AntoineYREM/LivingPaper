namespace LivingPaper.Application;

// The passages of a book as they travel in an exported file, with their vectors and the model that made them when the book
// has some: EmbeddingModel null and no vector otherwise, else one vector for each passage, in their order, all of the same
// size.
public sealed record PassageSet(IReadOnlyList<StoredPassage> Passages, string? EmbeddingModel, IReadOnlyList<float[]> Vectors)
{
    public override string ToString() => $"PassageSet {{ Passages = {Passages.Count}, Vectors = {Vectors.Count}, EmbeddingModel = {EmbeddingModel} }}";
}
