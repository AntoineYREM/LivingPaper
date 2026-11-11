using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

public sealed class JsonBookRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    // The fields of a complete book's file, in alphabetical order. A new field must be added here on purpose.
    private static readonly string[] DocumentedFields =
    [
        "cachedInputTokens", "characters", "fileName", "finishedAt", "id", "inputTokens", "model", "outputTokens",
        "partsDone", "partsSkipped", "partsTotal", "profilesDone", "profilesTotal", "providerName", "schemaVersion", "sentAt",
        "startedAt", "status", "summary", "title", "updatedAt",
    ];

    private readonly TempDirectory _temp = new();
    private readonly JsonBookRepository _repository;

    public JsonBookRepositoryTests() => _repository = new JsonBookRepository(_temp.BooksPath);

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Book NewBook(DateTimeOffset? sentAt = null) => new(BookId.New(), "Le Malade imaginaire", "malade.pdf", sentAt ?? Now);

    private static Book CompleteBook()
    {
        var book = NewBook();
        book.Summary = "France, 1673. A satirical comedy.";
        book.Status = BookStatus.Done;
        book.PartsDone = book.PartsTotal = 3;
        book.ProfilesDone = book.ProfilesTotal = 2;
        book.ProviderName = "OpenAI";
        book.Model = "gpt-5-nano";
        book.InputTokens = 1200;
        book.CachedInputTokens = 200;
        book.OutputTokens = 800;
        book.PartsSkipped = 1;
        book.StartedAt = Now.AddMinutes(1);
        book.UpdatedAt = Now.AddMinutes(5);
        book.FinishedAt = Now.AddMinutes(5);
        book.Characters.Add(new Character(1, "Argan", "A hypochondriac.", "I am Argan, and I am ill."));
        book.Characters.Add(new Character(2, "Toinette", "A sharp-tongued maid.", "I am Toinette."));
        return book;
    }

    private string FileOf(BookId id) => Path.Combine(_temp.BooksPath, id + ".json");

    [Fact]
    public async Task SaveThenGet_ReturnsTheSameBook()
    {
        var book = CompleteBook();

        await _repository.SaveAsync(book, Token);
        var loaded = await _repository.GetAsync(book.Id, Token);

        loaded.ShouldNotBeNull();
        loaded.ShouldBeEquivalentTo(book);
    }

    [Fact]
    public async Task SaveThenGet_KeepsAFailedBooksErrorAndAnEmptyBook()
    {
        var book = NewBook();
        book.Status = BookStatus.Failed;
        book.ErrorCode = "provider_unreachable";
        book.ErrorDetail = "The provider did not answer.";

        await _repository.SaveAsync(book, Token);
        var loaded = await _repository.GetAsync(book.Id, Token);

        loaded.ShouldNotBeNull();
        loaded.ShouldBeEquivalentTo(book);
        loaded.Summary.ShouldBeNull();
        loaded.Characters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_WritesOneReadableFileWithASchemaVersion()
    {
        var book = CompleteBook();

        await _repository.SaveAsync(book, Token);

        File.Exists(FileOf(book.Id)).ShouldBeTrue();
        var text = await File.ReadAllTextAsync(FileOf(book.Id), Token);
        using var json = JsonDocument.Parse(text);
        json.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("id").GetString().ShouldBe(book.Id.ToString());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Done");
        text.ShouldContain("\n");
    }

    [Fact]
    public async Task Save_WritesOnlyTheDocumentedFields_AndNoSecret()
    {
        var book = CompleteBook();

        await _repository.SaveAsync(book, Token);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(FileOf(book.Id), Token));
        json.RootElement.EnumerateObject().Select(property => property.Name).Order().ShouldBe(DocumentedFields);
    }

    [Fact]
    public async Task Save_ReplacesTheExistingFile()
    {
        var book = NewBook();
        await _repository.SaveAsync(book, Token);

        book.Status = BookStatus.Running;
        book.PartsDone = 2;
        await _repository.SaveAsync(book, Token);

        var loaded = await _repository.GetAsync(book.Id, Token);
        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe(BookStatus.Running);
        loaded.PartsDone.ShouldBe(2);
    }

    [Fact]
    public async Task Save_LeavesNoTemporaryFileBehind()
    {
        await _repository.SaveAsync(CompleteBook(), Token);
        await _repository.SaveAsync(CompleteBook(), Token);

        Directory.GetFiles(_temp.BooksPath).ShouldAllBe(file => file.EndsWith(".json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Save_WhenTheFileCannotBeReplaced_ThrowsAndRemovesItsTemporaryFile()
    {
        var book = NewBook();
        Directory.CreateDirectory(FileOf(book.Id)); // A directory where the file should go: the final rename must fail.

        // Windows reports "access denied" and Linux "is a directory": either way the save fails.
        await Should.ThrowAsync<Exception>(() => _repository.SaveAsync(book, Token));

        Directory.GetFileSystemEntries(_temp.BooksPath).ShouldBe([FileOf(book.Id)]);
    }

    [Fact]
    public async Task Save_RejectsATextLongerThanAllowed_AndKeepsTheExistingFileUntouched()
    {
        var book = CompleteBook();
        await _repository.SaveAsync(book, Token);
        var before = await File.ReadAllTextAsync(FileOf(book.Id), Token);

        book.Characters[0] = book.Characters[0] with { Name = new string('n', CharacterLimits.Name + 1) };
        var exception = await Should.ThrowAsync<ArgumentException>(() => _repository.SaveAsync(book, Token));

        exception.Message.ShouldContain("characters[0].name");
        (await File.ReadAllTextAsync(FileOf(book.Id), Token)).ShouldBe(before);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenThereIsNoSuchBook()
    {
        (await _repository.GetAsync(BookId.New(), Token)).ShouldBeNull();
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\": 999, \"id\": \"ID\"}")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"00000000000000000000000000000001\", \"title\": \"t\", \"fileName\": \"f\", \"status\": \"Done\", \"sentAt\": \"2026-11-11T12:00:00+00:00\", \"updatedAt\": \"2026-11-11T12:00:00+00:00\", \"characters\": []}")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"ID\", \"title\": \"t\", \"fileName\": \"f\", \"status\": \"Exploded\", \"sentAt\": \"2026-11-11T12:00:00+00:00\", \"updatedAt\": \"2026-11-11T12:00:00+00:00\", \"characters\": []}")]
    public async Task Get_ThrowsBookUnreadable_WhenTheFileIsCorruptOrNotUnderstood(string content)
    {
        var id = BookId.New();
        Directory.CreateDirectory(_temp.BooksPath);
        await File.WriteAllTextAsync(FileOf(id), content.Replace("ID", id.ToString(), StringComparison.Ordinal), Token);

        await Should.ThrowAsync<BookUnreadableException>(() => _repository.GetAsync(id, Token));
    }

    [Fact]
    public async Task Get_ThrowsBookUnreadable_WhenTheFileIsMuchBiggerThanAnyBook()
    {
        var id = BookId.New();
        Directory.CreateDirectory(_temp.BooksPath);
        await File.WriteAllTextAsync(FileOf(id), new string(' ', JsonBookRepository.MaxFileBytes + 1), Token);

        await Should.ThrowAsync<BookUnreadableException>(() => _repository.GetAsync(id, Token));
    }

    [Fact]
    public async Task Get_ThrowsBookUnreadable_WhenATextInTheFileIsLongerThanAllowed()
    {
        var book = CompleteBook();
        await _repository.SaveAsync(book, Token);
        var text = (await File.ReadAllTextAsync(FileOf(book.Id), Token)).Replace("Argan\"", new string('n', CharacterLimits.Name + 1) + "\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(FileOf(book.Id), text, Token);

        var exception = await Should.ThrowAsync<BookUnreadableException>(() => _repository.GetAsync(book.Id, Token));

        exception.Reason.ShouldContain("characters[0].name");
    }

    [Fact]
    public async Task List_ReturnsAnEmptyListWithoutError_WhenTheDirectoryDoesNotExistYet()
    {
        var listing = await _repository.ListAsync(Token);

        listing.Books.ShouldBeEmpty();
        listing.Unreadable.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_ReturnsTheMostRecentlySentBookFirst()
    {
        var old = NewBook(Now);
        var recent = NewBook(Now.AddDays(1));
        await _repository.SaveAsync(old, Token);
        await _repository.SaveAsync(recent, Token);

        var listing = await _repository.ListAsync(Token);

        listing.Books.Select(book => book.Id).ShouldBe([recent.Id, old.Id]);
    }

    [Fact]
    public async Task List_ReportsUnreadableFiles_WithoutFailingAndWithoutHidingTheOtherBooks()
    {
        var good = CompleteBook();
        await _repository.SaveAsync(good, Token);
        var corrupt = BookId.New();
        var stranger = BookId.New();
        await File.WriteAllTextAsync(FileOf(corrupt), "{ not json", Token);
        // A well-formed file that claims another id than its name: a copied or renamed file is not trusted.
        var copy = (await File.ReadAllTextAsync(FileOf(good.Id), Token)).Replace(good.Id.ToString(), BookId.New().ToString(), StringComparison.Ordinal);
        await File.WriteAllTextAsync(FileOf(stranger), copy, Token);

        var listing = await _repository.ListAsync(Token);

        listing.Books.Select(book => book.Id).ShouldBe([good.Id]);
        listing.Unreadable.Select(item => item.StorageName).Order().ShouldBe(new[] { corrupt + ".json", stranger + ".json" }.Order());
        listing.Unreadable.ShouldAllBe(item => item.Reason.Length > 0);
    }

    [Fact]
    public async Task List_IgnoresStepFilesTemporaryFilesAndFilesThatAreNotBooks()
    {
        var book = CompleteBook();
        await _repository.SaveAsync(book, Token);
        await File.WriteAllTextAsync(Path.Combine(_temp.BooksPath, book.Id + ".steps.json"), "{}", Token);
        await File.WriteAllTextAsync(Path.Combine(_temp.BooksPath, book.Id + ".json.abc.tmp"), "{", Token);
        await File.WriteAllTextAsync(Path.Combine(_temp.BooksPath, "notes.txt"), "hello", Token);
        await File.WriteAllTextAsync(Path.Combine(_temp.BooksPath, "readme.json"), "{}", Token);

        var listing = await _repository.ListAsync(Token);

        listing.Books.Select(item => item.Id).ShouldBe([book.Id]);
        listing.Unreadable.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_RemovesTheBookAndItsSteps_AndReturnsTrue()
    {
        var book = CompleteBook();
        await _repository.SaveAsync(book, Token);
        var stepsFile = Path.Combine(_temp.BooksPath, book.Id + ".steps.json");
        await File.WriteAllTextAsync(stepsFile, "{}", Token);

        (await _repository.DeleteAsync(book.Id, Token)).ShouldBeTrue();

        File.Exists(FileOf(book.Id)).ShouldBeFalse();
        File.Exists(stepsFile).ShouldBeFalse();
        (await _repository.GetAsync(book.Id, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task Delete_ReturnsFalse_WhenThereIsNoSuchBook()
    {
        (await _repository.DeleteAsync(BookId.New(), Token)).ShouldBeFalse();
    }
}
