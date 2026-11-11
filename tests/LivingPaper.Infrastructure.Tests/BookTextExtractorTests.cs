using System.Text;
using LivingPaper.Application;
using Shouldly;

namespace LivingPaper.Infrastructure.Tests;

// The text of an EPUB or a PDF, in reading order, and a clear reason for every file that cannot be read (PAPER-7).
public class BookTextExtractorTests
{
    private readonly BookTextExtractor _extractor = new();

    private BookText Extract(byte[] bytes, BookTextExtractor? extractor = null)
    {
        using var stream = new MemoryStream(bytes);
        return (extractor ?? _extractor).Extract(stream);
    }

    private void AssertRefused(byte[] bytes, BookRejection expected, BookTextExtractor? extractor = null) =>
        Should.Throw<BookReadException>(() => Extract(bytes, extractor)).Rejection.ShouldBe(expected);

    // ---- EPUB ----

    [Fact]
    public void Epub_TextInSpineOrder_WithTitle_WithoutTagsOrStyles()
    {
        var book = Extract(TestBooks.Epub());

        // Chapter one before chapter two even though the manifest lists them backwards; "../" and "%20" paths were found;
        // &nbsp; became a space; <br/> a new line; the <head> is gone.
        book.Title.ShouldBe("The Lighthouse");
        book.Text.ShouldBe("Chapter one: Lina arrives at the lighthouse.\nChapter two : Marc is waiting.\nThe end.");
        book.Text.ShouldNotContain("color");
        book.Text.ShouldNotContain("ignore");
    }

    [Fact]
    public void Epub_WithNoTitle_GivesNoTitle_SoTheCallerCanUseTheFileName() =>
        Extract(TestBooks.Epub(title: null)).Title.ShouldBeNull();

    [Fact]
    public void Epub_WithoutText_IsRefused() =>
        AssertRefused(TestBooks.Epub("Album", "<img src=\"a.jpg\"/>", "<div></div>"), BookRejection.NoText);

    [Fact]
    public void ZipThatIsNotAnEpub_IsRefusedAsDamagedEpub() =>
        AssertRefused(TestBooks.PlainZip(), BookRejection.DamagedEpub);

    [Fact]
    public void BrokenZip_IsRefusedAsDamagedEpub() =>
        AssertRefused([(byte)'P', (byte)'K', 3, 4, .. Encoding.ASCII.GetBytes(new string('x', 500))], BookRejection.DamagedEpub);

    [Fact]
    public void AZipBomb_IsRefused()
    {
        // 2 MB of text once unpacked, a few KB zipped; the limit of this extractor is 1 MB.
        var bomb = TestBooks.EpubUnpackingTo(2 * 1024 * 1024);

        bomb.Length.ShouldBeLessThan(20_000);
        AssertRefused(bomb, BookRejection.DamagedEpub, new BookTextExtractor(maxUnpackedBytes: 1024 * 1024));
    }

    [Fact]
    public void AnEpubJustUnderTheLimit_IsRead()
    {
        var book = Extract(TestBooks.EpubUnpackingTo(500 * 1024), new BookTextExtractor(maxUnpackedBytes: 1024 * 1024));

        book.Text.Length.ShouldBe(500 * 1024);
    }

    [Fact]
    public void AnXmlFileWithADocumentType_IsRefused_NeverResolved() =>
        // An external entity must never be read from the disk or the network: a DOCTYPE is refused outright.
        AssertRefused(TestBooks.EpubWithExternalEntity(), BookRejection.DamagedEpub);

    // ---- PDF ----

    [Fact]
    public void Pdf_TextPages_AreRead_SeparatedByAFormFeed()
    {
        var book = Extract(TestBooks.PdfTitled("A Day at Sea", TestBooks.TextPage, TestBooks.TextPage));

        book.Text.ShouldContain("Lina climbs the steps of the lighthouse");
        book.Text.Split('\f', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(2);
        book.Title.ShouldBe("A Day at Sea");
    }

    [Fact]
    public void Pdf_WithNoTitle_GivesNoTitle() =>
        Extract(TestBooks.Pdf(TestBooks.TextPage)).Title.ShouldBeNull();

    [Fact]
    public void Pdf_Scanned_IsRefused() =>
        // Three pages without text (pictures of pages in a real scanned book) and a short caption.
        AssertRefused(TestBooks.Pdf("", "", "Page 3", ""), BookRejection.ScannedPdf);

    [Fact]
    public void BrokenPdf_IsRefusedAsDamagedPdf() =>
        AssertRefused(Encoding.ASCII.GetBytes("%PDF-1.7\nthis is not really a PDF"), BookRejection.DamagedPdf);

    // ---- Neither ----

    [Theory]
    [InlineData("Just a text file.")]
    [InlineData("%PDX-1.7 almost a PDF")]
    [InlineData("<html><body>A web page</body></html>")]
    [InlineData("")]
    public void NeitherEpubNorPdf_IsRefused_WhateverItsName(string content) =>
        AssertRefused(Encoding.UTF8.GetBytes(content), BookRejection.Unsupported);

    [Fact]
    public void AFileThatOnlyStartsWithPK_IsNotTakenForAZip() =>
        AssertRefused(Encoding.ASCII.GetBytes("PK is the name of my band, not a zip"), BookRejection.Unsupported);

    [Fact]
    public void TheStreamIsLeftOpen_AndItsPositionDoesNotMatter()
    {
        using var stream = new MemoryStream(TestBooks.Epub());
        stream.Position = 10;

        _extractor.Extract(stream).Text.ShouldNotBeEmpty();

        stream.CanRead.ShouldBeTrue();
    }

    [Fact]
    public void TheErrorHoldsNothingFromTheFile()
    {
        var exception = Should.Throw<BookReadException>(() => Extract(TestBooks.Epub("T", "<img src=\"a.jpg\"/>")));

        exception.Message.ShouldNotContain("a.jpg");
    }

    // ---- HTML ----

    [Fact]
    public void HtmlToText_DecodesEntitiesAndDropsScripts() =>
        BookTextExtractor.HtmlToText("<script>alert(1)</script><h1>Title</h1><p>It&rsquo;s&nbsp;a   hot summer</p>").ShouldBe("Title\nIt’s a hot summer");
}
