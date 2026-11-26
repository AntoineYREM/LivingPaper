namespace LivingPaper.Application;

// Why the passages of a book have no vectors. The name is kept in the book file; the interface translates it.
public enum IndexFailure
{
    // No model for vectorization is chosen, or its key cannot be read.
    NoModel,
    ModelUnreachable,
    KeyRefused,

    // The provider refuses to make vectors with this model: it is not an embedding model, most of the time.
    CannotVectorize,
    ProviderError,
    InvalidReply,

    // The file of the passages cannot be written or read.
    IndexUnusable,
    Unexpected,

    // The vectors that came with a file are not those the model chosen makes of the same text.
    VectorsDiffer,
}

public enum PassagesState
{
    // The book has no passages: it was analyzed without a model for vectorization.
    Missing,

    // The passages have no vectors yet, or the last try failed.
    NotIndexed,

    // The vectors were made by another model than the one chosen.
    Outdated,
    Indexing,
    Indexed,

    // The vectors came with a file and have not been found to be those of the model chosen.
    Unchecked,
    Checking,
}

// What a book says about its passages. Model: the one that made the vectors, if any. Done and Total: while indexing.
public sealed record PassagesView(PassagesState State, int Passages, string? Model, IndexFailure? Failure, int Done, int Total);
