using System.IO.Compression;
using System.Text;

namespace LivingPaper.E2ETests;

// A small EPUB made for the tests, with an original text (nothing is under copyright). Two chapters, long enough to be read.
internal static class TestEpub
{
    public const string Title = "The Lighthouse Letters";

    public const string FileName = "lighthouse-letters.epub";

    public static byte[] Build()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // The first entry is "mimetype", stored without compression: that is what makes a file an EPUB.
            Add(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            Add(zip, "META-INF/container.xml", """
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
                </container>
                """);
            Add(zip, "OEBPS/content.opf", $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="id">urn:uuid:5b0f6f4e-0000-4000-8000-000000000001</dc:identifier>
                    <dc:title>{Title}</dc:title>
                    <dc:language>en</dc:language>
                  </metadata>
                  <manifest>
                    <item id="c1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
                    <item id="c2" href="chapter2.xhtml" media-type="application/xhtml+xml"/>
                  </manifest>
                  <spine><itemref idref="c1"/><itemref idref="c2"/></spine>
                </package>
                """);
            Add(zip, "OEBPS/chapter1.xhtml", Chapter("The island", Paragraphs(1)));
            Add(zip, "OEBPS/chapter2.xhtml", Chapter("The letters", Paragraphs(2)));
        }

        return stream.ToArray();
    }

    private static string Chapter(string title, string body) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>{title}</title></head><body><h1>{title}</h1>{body}</body></html>
        """;

    private static string Paragraphs(int chapter)
    {
        var text = new StringBuilder();
        for (var i = 1; i <= 12; i++)
        {
            text.Append("<p>")
                .Append(chapter == 1
                    ? $"Marguerite Voss climbed the stairs of the lighthouse for the {i}th time that evening, her lamp swinging against the cold wall. "
                      + "She did not like the sea, and the sea did not care. At the top she trimmed the wick, wrote the weather in her book, and thought of her brother far away."
                    : $"On Sunday number {i}, Captain Alder rowed to the island with a bundle of letters under his coat. He talked of old storms and old ships while Marguerite "
                      + "poured the tea, and she answered in short sentences, and neither of them said how much the visit mattered.")
                .Append("</p>");
        }

        return text.ToString();
    }

    private static void Add(ZipArchive zip, string name, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
