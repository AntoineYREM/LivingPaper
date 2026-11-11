using LivingPaper.Domain;

namespace LivingPaper.Application;

// A book found by id: in the library, or among the examples (which are read only).
public sealed record BookLookup(Book Book, bool IsExample);

public enum ExampleRejection
{
    // No example has this id.
    NotFound,

    // A book of the library already has the title of the example.
    Duplicate,
}

// The outcome of adding an example to the library: the copy, or the reason it was refused.
public sealed record AddExampleResult(Book? Added, ExampleRejection? Rejection)
{
    public bool Succeeded => Added is not null;
}
