using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// What the use cases of the books do with the passages and with the short summary of the plot (PAPER-84).
public sealed class BookServicePassageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();
    private readonly InMemoryBookTextStore _texts = new();
    private readonly FakeBookArchive _archive = new();
    private readonly FakePassageIndex _index = new();
    private readonly Book _example = Analyzed("an-example", "An Example");
    private readonly BookService _service;

    public BookServicePassageTests() => _service = new BookService(
        _books, _texts, new FakeBookTextExtractor(), new FakeAnalysisScheduler(), new FixedTimeProvider(Now), new AnalysisOptions(),
        new InMemoryExampleCatalog(_example), _archive, _index);

    public void Dispose() => _service.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Book Analyzed(string id, string title)
    {
        BookId.TryParse(id, out var bookId).ShouldBeTrue();
        var book = new Book(bookId, title, "book.epub", Now) { Status = BookStatus.Done, PlotSummary = "A plot.\nIt ends." };
        book.Characters.Add(new Character(1, "Argan", "The sick man.", "I am Argan."));
        return book;
    }

    private static BookKey Key(Book book) => new(BookShelf.Library, book.Id.ToString());

    private async Task<Book> InLibraryWithPassagesAsync(string id, string title)
    {
        var book = Analyzed(id, title);
        _books.Books[book.Id] = book;
        await _index.SavePassagesAsync(Key(book), [new StoredPassage(1, "A passage.", [])], Token);
        return book;
    }

    [Fact]
    public async Task DeletingABook_RemovesItsPassages()
    {
        var book = await InLibraryWithPassagesAsync("mine", "Mine");
        var other = await InLibraryWithPassagesAsync("other", "Other");

        (await _service.DeleteAsync(book.Id, Token)).ShouldBeTrue();

        _index.Books.Keys.ShouldBe([Key(other)]);
    }

    [Fact]
    public async Task ReplacingABook_RemovesThePassagesOfTheOldOne()
    {
        var old = await InLibraryWithPassagesAsync("the-lighthouse", "The Lighthouse");

        var result = await _service.AddAsync(new MemoryStream([1, 2, 3]), "lighthouse.epub", null, replace: true, Token);

        result.Added.ShouldNotBeNull().Id.ShouldBe(old.Id);
        _index.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnIndexThatFails_DoesNotKeepABookFromBeingDeleted()
    {
        var book = await InLibraryWithPassagesAsync("mine", "Mine");
        _index.Failure = new PassageIndexException("locked");

        (await _service.DeleteAsync(book.Id, Token)).ShouldBeTrue();

        _books.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnIndexThatCannotBeUsed_IsNotCalled()
    {
        var book = await InLibraryWithPassagesAsync("mine", "Mine");
        _index.Problem = PassageIndexProblem.ExtensionMissing;

        (await _service.DeleteAsync(book.Id, Token)).ShouldBeTrue();

        _index.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheShortSummaryOfThePlot_FollowsABook_FromAnExample_AndThroughAnExportAndAnImport()
    {
        var copy = (await _service.AddExampleAsync(_example.Id, Token)).Added.ShouldNotBeNull();
        var file = (await _service.ExportAsync(copy.Id, Token)).ShouldNotBeNull();
        await _service.DeleteAsync(copy.Id, Token);

        var imported = await _service.ImportAsync(new MemoryStream(file.Content), replace: false, Token);

        copy.PlotSummary.ShouldBe(_example.PlotSummary);
        imported.Imported.ShouldNotBeNull().PlotSummary.ShouldBe(_example.PlotSummary);
    }

    [Fact]
    public async Task AnImportedPlotSummaryWithAControlCharacter_IsRefused()
    {
        var book = Analyzed("imported", "Imported");
        book.PlotSummary = "A plot\u0007.";

        var result = await _service.ImportAsync(new MemoryStream(_archive.As(new ArchiveParseResult(book, null))), replace: false, Token);

        result.Rejection.ShouldBe(ImportRejection.Invalid);
        result.Detail.ShouldBe("plotSummary");
    }
}
