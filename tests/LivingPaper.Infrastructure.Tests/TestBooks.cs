using System.Globalization;
using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace LivingPaper.Infrastructure.Tests;

// Small books made in memory: no real book is committed to the repository. An EPUB is a zip of XHTML pages; a PDF is made
// with PdfPig's writer (the library that reads them).
internal static class TestBooks
{
    // About 250 letters: comfortably above the 100 letters per page of a real page of text.
    public const string TextPage =
        "Lina climbs the steps of the lighthouse every evening. Marc watches her from the harbour and wonders why she " +
        "never speaks of her brother. The storm is coming, the boats come home, and the light turns above the sea.";

    // An EPUB with its pages in a sub-folder ("OEBPS"), listed in the manifest in another order than the reading order
    // (the spine), one page reached through "../", one with a URL-encoded name, and HTML entities such as &nbsp;.
    public static byte[] Epub(string? title = "The Lighthouse", params string[] chapters)
    {
        if (chapters.Length == 0)
        {
            chapters = ["<p>Chapter one: Lina arrives at the lighthouse.</p>", "<p>Chapter two&nbsp;: Marc is waiting.<br/>The end.</p>"];
        }

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "mimetype", "application/epub+zip");
            Add(zip, "META-INF/container.xml", """
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
                </container>
                """);

            var manifest = new StringBuilder();
            var spine = new StringBuilder();
            for (var i = chapters.Length - 1; i >= 0; i--)
            {
                manifest.Append(CultureInfo.InvariantCulture, $"""<item id="c{i}" href="{HrefFor(i)}" media-type="application/xhtml+xml"/>""");
            }

            for (var i = 0; i < chapters.Length; i++)
            {
                spine.Append(CultureInfo.InvariantCulture, $"""<itemref idref="c{i}"/>""");
                Add(zip, PathFor(i), $"""
                    <html xmlns="http://www.w3.org/1999/xhtml"><head><title>ignore</title><style>p {"{"} color: red; {"}"}</style></head>
                    <body>{chapters[i]}</body></html>
                    """);
            }

            var titleElement = title is null ? string.Empty : $"<dc:title>{title}</dc:title>";
            Add(zip, "OEBPS/content.opf", $"""
                <?xml version="1.0"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">{titleElement}</metadata>
                  <manifest>{manifest}</manifest>
                  <spine>{spine}</spine>
                </package>
                """);
        }

        return memory.ToArray();
    }

    // Page 0 in "Text/../Text/" (a "../" to resolve), page 1 with a space in its name, the others plain.
    private static string HrefFor(int index) => index switch
    {
        0 => "Text/../Text/ch0.xhtml",
        1 => "Text/Chapter%201.xhtml",
        _ => $"Text/ch{index}.xhtml",
    };

    private static string PathFor(int index) => index switch
    {
        0 => "OEBPS/Text/ch0.xhtml",
        1 => "OEBPS/Text/Chapter 1.xhtml",
        _ => $"OEBPS/Text/ch{index}.xhtml",
    };

    // A zip that is not an EPUB (no META-INF/container.xml).
    public static byte[] PlainZip()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "notes.txt", "nothing");
        }

        return memory.ToArray();
    }

    // An EPUB whose only page is "size" bytes of text once unpacked but a few KB zipped: what a zip bomb looks like.
    public static byte[] EpubUnpackingTo(int size)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "META-INF/container.xml", """<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="content.opf"/></rootfiles></container>""");
            Add(zip, "content.opf", """<package xmlns="http://www.idpf.org/2007/opf"><manifest><item id="a" href="a.xhtml"/></manifest><spine><itemref idref="a"/></spine></package>""");
            Add(zip, "a.xhtml", "<p>" + new string('a', size) + "</p>");
        }

        return memory.ToArray();
    }

    // An EPUB whose container.xml carries a DOCTYPE with an external entity.
    public static byte[] EpubWithExternalEntity()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "META-INF/container.xml", """<?xml version="1.0"?><!DOCTYPE c [<!ENTITY x SYSTEM "file:///etc/passwd">]><container><rootfiles><rootfile full-path="&x;"/></rootfiles></container>""");
        }

        return memory.ToArray();
    }

    // A PDF whose pages hold these texts (plain ASCII). A page given as "" has no text at all, like a scanned page.
    public static byte[] Pdf(params string[] pages) => PdfTitled(null, pages);

    public static byte[] PdfTitled(string? title, params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        if (title is not null)
        {
            builder.DocumentInformation.Title = title;
        }

        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 780.0;
            foreach (var line in Chunk(text, 80))
            {
                page.AddText(line, 10, new PdfPoint(40, y), font);
                y -= 14;
            }
        }

        return builder.Build();
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var start = 0; start < text.Length; start += size)
        {
            yield return text.Substring(start, Math.Min(size, text.Length - start));
        }
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
