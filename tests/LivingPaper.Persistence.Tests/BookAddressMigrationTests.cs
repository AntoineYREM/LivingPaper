using System.Text.Json;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

public sealed class BookAddressMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private string Books => _temp.BooksPath;

    private string Uploads => Path.Combine(_temp.Path, "uploads");

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string WriteBook(string id, string title, int schemaVersion = 2, DateTimeOffset? sentAt = null)
    {
        Directory.CreateDirectory(Books);
        var path = Path.Combine(Books, id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion,
            id,
            title,
            fileName = "x.epub",
            status = "Done",
            sentAt = sentAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            note = "kept as it is",
            characters = Array.Empty<object>(),
        }));
        return path;
    }

    private Task<IReadOnlyList<(string From, string To)>> RunAsync(params string[] reserved) =>
        BookAddressMigration.RunAsync(Books, Uploads, reserved, Token);

    [Fact]
    public async Task ABookWithAnOldId_IsRenamedAfterItsTitle_InItsFileAndInsideIt()
    {
        var old = BookId.New().ToString();
        WriteBook(old, "Cyrano de Bergerac");

        var moved = await RunAsync();

        moved.ShouldBe([(old, "cyrano-de-bergerac")]);
        File.Exists(Path.Combine(Books, old + ".json")).ShouldBeFalse();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Books, "cyrano-de-bergerac.json")));
        document.RootElement.GetProperty("id").GetString().ShouldBe("cyrano-de-bergerac");
        document.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(3);
        document.RootElement.GetProperty("note").GetString().ShouldBe("kept as it is");
    }

    [Fact]
    public async Task TheRenamedBook_IsReadByTheRepository()
    {
        var old = BookId.New().ToString();
        WriteBook(old, "Cyrano de Bergerac");
        await RunAsync();

        var book = await new JsonBookRepository(Books).GetAsync(BookId.FromTitle("Cyrano de Bergerac", _ => false), Token);

        book.ShouldNotBeNull();
        book.Title.ShouldBe("Cyrano de Bergerac");
    }

    [Fact]
    public async Task TheStepsAndTheWaitingText_FollowTheBook()
    {
        var old = BookId.New().ToString();
        WriteBook(old, "Alice");
        File.WriteAllText(Path.Combine(Books, old + ".steps.json"), "{}");
        Directory.CreateDirectory(Uploads);
        File.WriteAllText(Path.Combine(Uploads, old + ".txt"), "text");

        await RunAsync();

        File.Exists(Path.Combine(Books, "alice.steps.json")).ShouldBeTrue();
        File.Exists(Path.Combine(Uploads, "alice.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(Books, old + ".steps.json")).ShouldBeFalse();
        File.Exists(Path.Combine(Uploads, old + ".txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task TwoBooksOfTheSameSlug_TheOldestKeepsTheAddress_TheOtherGetsANumber()
    {
        var first = BookId.New().ToString();
        var second = BookId.New().ToString();
        WriteBook(second, "Alice", sentAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        WriteBook(first, "ALICE!", sentAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var moved = await RunAsync();

        moved.ShouldBe([(first, "alice"), (second, "alice-2")], ignoreOrder: true);
    }

    [Fact]
    public async Task ABookDoesNotTakeTheAddressOfAnExample_OrOfAnotherFile()
    {
        var old = BookId.New().ToString();
        WriteBook(old, "Cyrano de Bergerac");
        WriteBook("cyrano-de-bergerac-2", "Something else", schemaVersion: 3);

        var moved = await RunAsync("cyrano-de-bergerac");

        moved.ShouldBe([(old, "cyrano-de-bergerac-3")]);
    }

    [Fact]
    public async Task ABookThatAlreadyHasASlug_IsLeftAlone_AndRunningAgainChangesNothing()
    {
        WriteBook("alice", "Alice", schemaVersion: 3);
        WriteBook(BookId.New().ToString(), "Cyrano");

        (await RunAsync()).Count.ShouldBe(1);
        (await RunAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"title\": \"No id\" }")]
    [InlineData("[]")]
    public async Task AFileThatIsNotABook_IsLeftWhereItIs(string content)
    {
        var old = BookId.New().ToString();
        Directory.CreateDirectory(Books);
        var path = Path.Combine(Books, old + ".json");
        File.WriteAllText(path, content);

        (await RunAsync()).ShouldBeEmpty();
        File.ReadAllText(path).ShouldBe(content);
    }

    [Fact]
    public async Task AFileWhoseIdIsNotItsName_IsLeftWhereItIs()
    {
        var old = BookId.New().ToString();
        var path = WriteBook(old, "Alice");
        File.WriteAllText(path, File.ReadAllText(path).Replace(old, BookId.New().ToString(), StringComparison.Ordinal));

        (await RunAsync()).ShouldBeEmpty();
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public async Task WithNoBooksDirectory_NothingHappens()
    {
        (await RunAsync()).ShouldBeEmpty();
    }
}
