namespace LivingPaper.Application;

// Why a book was not accepted. A stable key: the interface turns each one into a sentence in the user's language.
public enum BookRejection
{
    // No file was sent, or it has no content.
    Empty,

    // Larger than BookService.MaxBookBytes.
    TooLarge,

    // Neither an EPUB nor a PDF, whatever its name says: the kind is read from the first bytes of the content.
    Unsupported,

    // A PDF made of pictures of pages, with almost no text. An EPUB of the same book works.
    ScannedPdf,

    // A zip that is not a readable EPUB, or one that unpacks to far more than a book can hold (a "zip bomb").
    DamagedEpub,

    // A PDF that is broken, encrypted or protected.
    DamagedPdf,

    // A readable file with no words in it.
    NoText,

    // A book with the same title is already there: tick "Replace" to replace its analysis.
    Duplicate,
}
