using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LivingPaper.Application;
using UglyToad.PdfPig;

namespace LivingPaper.Infrastructure;

// Reads the text of an EPUB or a PDF. The kind of file is recognised from the first bytes of its content, never from its
// name: a text file renamed ".pdf" is not taken for a PDF.
//   - EPUB: a zip of XHTML pages. content.opf lists them in reading order (the "spine").
//   - PDF: PdfPig reads the words of each page. A scanned PDF has pictures of pages and almost no text: refused.
// Nothing of the file (text, title, names inside the zip) is put in an error or a log.
public sealed partial class BookTextExtractor(long maxUnpackedBytes = BookTextExtractor.DefaultMaxUnpackedBytes) : IBookTextExtractor
{
    // A zip can hold a small file that becomes huge once unpacked (a "zip bomb"). The text of a novel is a few MB.
    public const long DefaultMaxUnpackedBytes = 100L * 1024 * 1024;

    // Below this many letters per page, a PDF is a scanned book: a real page of text has well over 100.
    public const int MinLettersPerPdfPage = 100;

    // container.xml and content.opf are small; a bigger one is not a book.
    private const long MaxXmlBytes = 5L * 1024 * 1024;

    public BookText Extract(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        content.Position = 0;
        var start = new byte[5];
        var read = content.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
        content.Position = 0;

        // A zip file starts with "PK" and the bytes 3 and 4, a PDF with "%PDF-".
        if (read >= 4 && start[0] == 'P' && start[1] == 'K' && start[2] == 3 && start[3] == 4)
        {
            return ReadEpub(content);
        }

        if (read >= 5 && Encoding.ASCII.GetString(start, 0, 5) == "%PDF-")
        {
            return ReadPdf(content);
        }

        throw new BookReadException(BookRejection.Unsupported);
    }

    private BookText ReadEpub(Stream stream)
    {
        // Counts the bytes really unpacked, not the size the zip announces: an announced size can be forged.
        var budget = maxUnpackedBytes;
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            // META-INF/container.xml says where the package file (content.opf) is.
            var container = LoadXml(zip, "META-INF/container.xml", ref budget);
            var opfPath = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value
                ?? throw new BookReadException(BookRejection.DamagedEpub);
            var opf = LoadXml(zip, opfPath, ref budget);
            var opfFolder = opfPath.Contains('/', StringComparison.Ordinal) ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : string.Empty;

            // The manifest lists every file with an id; the spine gives the reading order as a list of ids.
            var files = opf.Descendants().Where(e => e.Name.LocalName == "item")
                .Where(e => e.Attribute("id") is not null && e.Attribute("href") is not null)
                .GroupBy(e => e.Attribute("id")!.Value)
                .ToDictionary(group => group.Key, group => group.First().Attribute("href")!.Value);
            var order = opf.Descendants().Where(e => e.Name.LocalName == "itemref")
                .Select(e => e.Attribute("idref")?.Value)
                .Where(id => id is not null && files.ContainsKey(id))
                .Select(id => ResolvePath(opfFolder, files[id!]));

            var title = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim();

            var text = new StringBuilder();
            foreach (var path in order)
            {
                var entry = zip.GetEntry(path);
                if (entry is null)
                {
                    continue; // A missing page should not stop the whole book.
                }

                var page = ReadBounded(entry, ref budget);
                text.Append(HtmlToText(Encoding.UTF8.GetString(page))).Append('\n');
            }

            return Checked(title, text.ToString(), BookRejection.NoText);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or NotSupportedException)
        {
            throw new BookReadException(BookRejection.DamagedEpub);
        }
    }

    private static BookText ReadPdf(Stream stream)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(stream);
        }
        catch (Exception)
        {
            // PdfPig throws several kinds of exception for a broken, encrypted or unusual file: all mean "cannot read it".
            throw new BookReadException(BookRejection.DamagedPdf);
        }

        using (document)
        {
            var text = new StringBuilder();
            try
            {
                foreach (var page in document.GetPages())
                {
                    // Words joined by spaces: a line break inside a paragraph does not matter to the AI. Pages are
                    // separated by a form feed so the analysis can cut the book by real pages.
                    text.AppendJoin(' ', page.GetWords().Select(word => word.Text)).Append('\f');
                }
            }
            catch (Exception)
            {
                throw new BookReadException(BookRejection.DamagedPdf);
            }

            var letters = text.ToString().Count(char.IsLetter);
            if (document.NumberOfPages == 0 || letters < MinLettersPerPdfPage * document.NumberOfPages)
            {
                throw new BookReadException(BookRejection.ScannedPdf);
            }

            return Checked(document.Information.Title?.Trim(), text.ToString(), BookRejection.ScannedPdf);
        }
    }

    private static BookText Checked(string? title, string text, BookRejection emptyRejection)
    {
        var cleaned = text.Trim();
        if (!cleaned.Any(char.IsLetter))
        {
            throw new BookReadException(emptyRejection);
        }

        return new BookText(string.IsNullOrWhiteSpace(title) ? null : title, cleaned);
    }

    // The XML of an EPUB is read with documents types refused: nothing is ever fetched from the disk or the network.
    private static XDocument LoadXml(ZipArchive zip, string path, ref long budget)
    {
        var entry = zip.GetEntry(path) ?? throw new BookReadException(BookRejection.DamagedEpub);
        var bytes = ReadBounded(entry, ref budget, MaxXmlBytes);
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    // Unpacks an entry, stopping as soon as more bytes than the budget have come out. The budget is shared by the whole
    // book, so a hundred small pages cannot add up to more than a book can hold either.
    private static byte[] ReadBounded(ZipArchiveEntry entry, ref long budget, long? entryLimit = null)
    {
        var limit = Math.Min(budget, entryLimit ?? long.MaxValue);
        using var source = entry.Open();
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > limit)
            {
                throw new BookReadException(BookRejection.DamagedEpub);
            }

            memory.Write(buffer, 0, read);
        }

        budget -= memory.Length;
        return memory.ToArray();
    }

    // The href in content.opf is relative to content.opf's folder and may be URL-encoded ("Chapter%201.xhtml") or go up a
    // folder ("../Text/ch1.xhtml"). Zip entry names use "/" and no "..".
    private static string ResolvePath(string folder, string href)
    {
        var parts = new List<string>();
        foreach (var part in (folder + WebUtility.UrlDecode(href.Split('#')[0])).Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (part is not ("" or "."))
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }

    // Plain text from an XHTML page. Regular expressions rather than an XML parser, because real EPUB pages often use HTML
    // entities such as &nbsp; that an XML parser refuses. Paragraphs and line breaks become new lines.
    public static string HtmlToText(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var text = HeadOrScriptRegex().Replace(html, " ");
        text = BlockEndRegex().Replace(text, "\n");
        text = TagRegex().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        var lines = text.Split('\n')
            .Select(line => SpacesRegex().Replace(line, " ").Trim())
            .Where(line => line.Length > 0);
        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"<(head|script|style)\b.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex HeadOrScriptRegex();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|h[1-6]|li|tr|blockquote|section)\s*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex SpacesRegex();
}
