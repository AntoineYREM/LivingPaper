namespace LivingPaper.Application;

// Reads the text and the title of a book file (EPUB or PDF). The kind of file is recognised from its content, not from its
// name. The stream must be seekable and is left open.
public interface IBookTextExtractor
{
    // Throws BookReadException (Unsupported, ScannedPdf, DamagedEpub, DamagedPdf or NoText) when the file cannot be used.
    BookText Extract(Stream content);
}
