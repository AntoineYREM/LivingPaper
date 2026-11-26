namespace LivingPaper.Application;

// Where a book lives: an example and a book of the library can have the same address, and each keeps its own passages.
public enum BookShelf
{
    Library,
    Examples,
}

public sealed record BookKey(BookShelf Shelf, string Id);

// A scene a passage belongs to: what happens, and who is there.
public sealed record PassageScene(string Summary, IReadOnlyList<string> Present);

// A passage as it is kept. Scenes: none when the analysis did not tell. The text is never logged.
public sealed record StoredPassage(int Number, string Text, IReadOnlyList<PassageScene> Scenes)
{
    public override string ToString() => $"StoredPassage {{ Number = {Number}, Length = {Text.Length}, Scenes = {Scenes.Count} }}";
}

// Distance: the cosine distance to the vector searched, from 0 (the same direction) to 2 (the opposite one).
public sealed record FoundPassage(StoredPassage Passage, double Distance);

// EmbeddingModel: null, and VectorSize 0, when the passages have no vectors yet. Unchecked: the vectors came with a file
// and are not to be used before they are checked.
public sealed record PassageIndexState(int Passages, string? EmbeddingModel, int VectorSize, bool Unchecked = false);

public enum PassageIndexProblem
{
    // SQLite or its vector extension cannot be loaded: some platforms have no binary.
    ExtensionMissing,

    // The file of the index cannot be read: damaged, written by a later version, or not reachable. It is never removed.
    FileUnusable,
}

public sealed class PassageIndexException(string message, Exception? inner = null) : Exception(message, inner);
