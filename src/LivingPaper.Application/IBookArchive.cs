using LivingPaper.Domain;

namespace LivingPaper.Application;

// Why a file sent to be imported was refused. A stable key: the interface turns each one into a sentence in the user's language.
public enum ImportRejection
{
    // No file was sent, or it is empty.
    Empty,

    // Larger than the limit of a book file.
    TooLarge,

    // Not JSON, or JSON whose values are not of the types of a book.
    NotJson,

    // A schemaVersion that this application does not know (older and no longer read, or not a number it ever wrote).
    UnknownVersion,

    // A schemaVersion newer than this application knows: the book was exported by a newer LivingPaper.
    NewerVersion,

    // A required field is missing, a number is out of range, a text is longer than allowed, or a rule of a book is broken.
    // The detail names the field, never what was written in it.
    Invalid,

    // A valid book, but not an analyzed one: only a finished analysis can be imported.
    NotAnalyzed,

    // A book of the library has the same identifier or the same title: tick "Replace" to replace it.
    Conflict,
}

// The result of reading a file to import: the book, or why not.
public sealed record ArchiveParseResult(Book? Book, ImportRejection? Rejection, string? Detail = null);

// The file format of an exported book: the same as a book of the library (data/books/{id}.json) and of an example. A port so the
// use cases do not know the file format, and so it can be faked.
public interface IBookArchive
{
    // The file of an analyzed book, as bytes. Built from the fields of the book one by one, so it can hold nothing else: no key,
    // no setting, no path of the machine, no text of the book.
    byte[] Export(Book book);

    // Reads a file that someone sent: untrusted input, validated before anything is accepted (size, JSON, schemaVersion, required
    // fields, ranges, lengths of the texts). Never throws for a bad file: the result says why.
    ArchiveParseResult Parse(byte[] content);
}
