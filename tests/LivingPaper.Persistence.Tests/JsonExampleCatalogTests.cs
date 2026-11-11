using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// The examples shipped with the application (PAPER-11): read like any book from disk, with their own rules.
public sealed class JsonExampleCatalogTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly string _examples;

    public JsonExampleCatalogTests()
    {
        _examples = Path.Combine(_temp.Path, "examples");
        Directory.CreateDirectory(_examples);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Book Analyzed(string title, BookId? id = null)
    {
        var book = new Book(id ?? BookId.New(), title, "book.pdf", Now) { Status = BookStatus.Done, Summary = "A summary." };
        book.Characters.Add(new Character(1, "Argan", "The invalid.", "I am Argan."));
        return book;
    }

    // Writes the book as the repository does, then gives the file the name an example would have.
    private async Task<string> WriteExampleAsync(Book book, string fileName)
    {
        var repository = new JsonBookRepository(_temp.BooksPath);
        await repository.SaveAsync(book, Token);
        var path = Path.Combine(_examples, fileName);
        File.Move(Path.Combine(_temp.BooksPath, book.Id + ".json"), path, overwrite: true);
        return path;
    }

    private JsonExampleCatalog NewCatalog() => new(_examples);

    [Fact]
    public async Task AnExample_IsReadWhateverItsFileIsNamed_AndListedByTitle()
    {
        var zola = Analyzed("Zola");
        var alice = Analyzed("alice in wonderland");
        await WriteExampleAsync(zola, "zola.json");
        await WriteExampleAsync(alice, "an-example-about-alice.json");

        var listing = await NewCatalog().ListAsync(Token);

        listing.Books.Select(book => book.Title).ShouldBe(["alice in wonderland", "Zola"]);
        listing.Unreadable.ShouldBeEmpty();
        listing.Books[0].Id.ShouldBe(alice.Id);
        listing.Books[0].Characters.Single().Name.ShouldBe("Argan");
    }

    [Fact]
    public async Task GetByIdFindsAnExample_AndAnUnknownIdIsNull()
    {
        var book = Analyzed("Le Malade imaginaire");
        await WriteExampleAsync(book, "malade.json");
        var catalog = NewCatalog();

        (await catalog.GetAsync(book.Id, Token))!.Title.ShouldBe("Le Malade imaginaire");
        (await catalog.GetAsync(BookId.New(), Token)).ShouldBeNull();
    }

    [Fact]
    public async Task ADirectoryThatDoesNotExist_Or_IsEmpty_GivesNoExample()
    {
        (await new JsonExampleCatalog(Path.Combine(_temp.Path, "nothing")).ListAsync(Token)).Books.ShouldBeEmpty();
        (await NewCatalog().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnInvalidExample_IsLeftOutWithItsReason_AndTheOthersAreKept()
    {
        await WriteExampleAsync(Analyzed("Good"), "good.json");
        await File.WriteAllTextAsync(Path.Combine(_examples, "broken.json"), "{ SECRET-LOOKING TEXT, not json", Token);
        await File.WriteAllTextAsync(Path.Combine(_examples, "empty.json"), "null", Token);
        await File.WriteAllTextAsync(Path.Combine(_examples, "no-id.json"), """{"schemaVersion":1,"title":"x"}""", Token);
        await File.WriteAllTextAsync(Path.Combine(_examples, "future.json"), """{"schemaVersion":99,"id":"0123456789abcdef0123456789abcdef"}""", Token);

        var listing = await NewCatalog().ListAsync(Token);

        listing.Books.Select(book => book.Title).ShouldBe(["Good"]);
        listing.Unreadable.Select(item => item.StorageName).Order().ShouldBe(["broken.json", "empty.json", "future.json", "no-id.json"]);
        listing.Unreadable.Single(item => item.StorageName == "broken.json").Reason.ShouldNotContain("SECRET");
        listing.Unreadable.Single(item => item.StorageName == "future.json").Reason.ShouldContain("schemaVersion");
        listing.Unreadable.Single(item => item.StorageName == "no-id.json").Reason.ShouldContain("id");
    }

    [Fact]
    public async Task ATextLongerThanAllowed_IsRefused_LikeForAnyBookRead()
    {
        var book = Analyzed("Long");
        var path = await WriteExampleAsync(book, "long.json");
        var json = (await File.ReadAllTextAsync(path, Token)).Replace("I am Argan.", new string('x', CharacterLimits.ChatProfile + 1), StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, json, Token);

        var listing = await NewCatalog().ListAsync(Token);

        listing.Books.ShouldBeEmpty();
        listing.Unreadable.Single().Reason.ShouldContain("longer than allowed");
    }

    [Theory]
    [InlineData(BookStatus.Waiting)]
    [InlineData(BookStatus.Running)]
    [InlineData(BookStatus.Failed)]
    public async Task AnExample_MustBeAnAnalyzedBook(BookStatus status)
    {
        var book = Analyzed("Not analyzed");
        book.Status = status;
        await WriteExampleAsync(book, "x.json");

        var listing = await NewCatalog().ListAsync(Token);

        listing.Books.ShouldBeEmpty();
        listing.Unreadable.Single().Reason.ShouldContain("analyzed");
    }

    [Fact]
    public async Task AnExampleWithNoCharacter_IsLeftOut()
    {
        var book = Analyzed("Nobody");
        book.Characters.Clear();
        await WriteExampleAsync(book, "nobody.json");

        (await NewCatalog().ListAsync(Token)).Unreadable.Single().Reason.ShouldContain("character");
    }

    [Fact]
    public async Task TwoExamplesWithTheSameId_KeepTheFirstByName_AndLeaveTheOtherOut()
    {
        var first = Analyzed("First");
        var second = Analyzed("Second", first.Id);
        await WriteExampleAsync(first, "a.json");
        await WriteExampleAsync(second, "b.json");

        var listing = await NewCatalog().ListAsync(Token);

        listing.Books.Single().Title.ShouldBe("First");
        listing.Unreadable.Single().StorageName.ShouldBe("b.json");
    }

    [Fact]
    public async Task AFileThatIsTooLarge_IsLeftOut_WithoutBeingRead()
    {
        await File.WriteAllTextAsync(Path.Combine(_examples, "huge.json"), new string(' ', JsonBookRepository.MaxFileBytes + 1), Token);

        (await NewCatalog().ListAsync(Token)).Unreadable.Single().Reason.ShouldContain("too large");
    }

    [Fact]
    public async Task OtherFiles_AreIgnored_StepsFilesTextFilesAndFolders()
    {
        await WriteExampleAsync(Analyzed("Good"), "good.json");
        await File.WriteAllTextAsync(Path.Combine(_examples, "notes.txt"), "not an example", Token);
        await File.WriteAllTextAsync(Path.Combine(_examples, "0123456789abcdef0123456789abcdef.steps.json"), "{}", Token);
        Directory.CreateDirectory(Path.Combine(_examples, "folder.json"));

        var listing = await NewCatalog().ListAsync(Token);

        listing.Books.Count.ShouldBe(1);
        listing.Unreadable.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheDirectory_IsReadOnce_AndKept()
    {
        await WriteExampleAsync(Analyzed("First"), "first.json");
        var catalog = NewCatalog();
        (await catalog.ListAsync(Token)).Books.Count.ShouldBe(1);

        await WriteExampleAsync(Analyzed("Second"), "second.json");

        (await catalog.ListAsync(Token)).Books.Count.ShouldBe(1); // The examples do not change while the application runs.
    }

    [Fact]
    public async Task ManyReadsAtOnce_ReadTheDirectoryOnce()
    {
        await WriteExampleAsync(Analyzed("First"), "first.json");
        var catalog = NewCatalog();

        var listings = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => catalog.ListAsync(Token)));

        listings.Select(listing => listing.Books.Count).ShouldAllBe(count => count == 1);
        listings.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task AFileTakenFromTheLibrary_NamedAfterItsId_IsAnExampleToo()
    {
        var book = Analyzed("Exported");
        await new JsonBookRepository(_temp.BooksPath).SaveAsync(book, Token);
        File.Copy(Path.Combine(_temp.BooksPath, book.Id + ".json"), Path.Combine(_examples, book.Id + ".json"));

        (await NewCatalog().ListAsync(Token)).Books.Single().Id.ShouldBe(book.Id);
    }
}
