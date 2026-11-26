using System.Text.Json;
using System.Text.Json.Nodes;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// PAPER-88: an example shipped with its passages and vectors.
public sealed class ExamplePassagesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly JsonBookArchive _archive = new();

    public ExamplePassagesTests() => Directory.CreateDirectory(_temp.Path);

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Book Analyzed(string title)
    {
        var book = new Book(BookId.FromTitle(title, _ => false), title, "book.epub", Now) { Status = BookStatus.Done, UpdatedAt = Now };
        book.Characters.Add(new Character(1, "Emma", "A doctor's wife.", "I am Emma."));
        return book;
    }

    private static PassageSet Set(int passages = 2, int letters = 20) => new(
        [.. Enumerable.Range(1, passages).Select(number => new StoredPassage(number, new string('a', letters), []))],
        "bge-m3",
        [.. Enumerable.Range(1, passages).Select(number => new float[] { number, 1 })]);

    private async Task WriteAsync(string fileName, byte[] content) => await File.WriteAllBytesAsync(Path.Combine(_temp.Path, fileName), content, Token);

    [Fact]
    public async Task AnExampleWithPassages_TellsHowMany_AndGivesThem()
    {
        var book = Analyzed("Madame Bovary");
        await WriteAsync("bovary.json", _archive.ExportWithPassages(book, Set(3)).Content);
        var catalog = new JsonExampleCatalog(_temp.Path);

        (await catalog.ListAsync(Token)).Books.ShouldHaveSingleItem();
        (await catalog.CountPassagesAsync(book.Id, Token)).ShouldBe(3);
        var set = (await catalog.GetPassagesAsync(book.Id, Token))!;
        set.EmbeddingModel.ShouldBe("bge-m3");
        set.Passages.Count.ShouldBe(3);
        set.Vectors[2].ShouldBe([3f, 1f]);
    }

    [Fact]
    public async Task AnExampleWithoutPassages_AndAnUnknownOne_HaveNone()
    {
        var book = Analyzed("Cyrano");
        await WriteAsync("cyrano.json", _archive.Export(book));
        var catalog = new JsonExampleCatalog(_temp.Path);

        (await catalog.CountPassagesAsync(book.Id, Token)).ShouldBe(0);
        (await catalog.GetPassagesAsync(book.Id, Token)).ShouldBeNull();
        var unknown = BookId.FromTitle("No such book", _ => false);
        (await catalog.CountPassagesAsync(unknown, Token)).ShouldBe(0);
        (await catalog.GetPassagesAsync(unknown, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AnExampleWithPassages_MayBeLargerThanABook()
    {
        var book = Analyzed("A long book");
        await WriteAsync("long.json", _archive.ExportWithPassages(book, Set(passages: 700, letters: 1900)).Content);
        var catalog = new JsonExampleCatalog(_temp.Path);

        (await catalog.ListAsync(Token)).Unreadable.ShouldBeEmpty();
        (await catalog.CountPassagesAsync(book.Id, Token)).ShouldBe(700);
    }

    [Fact]
    public async Task AnExampleWithAWrongPassage_IsLeftOut_AndTheFieldIsNamed()
    {
        var file = JsonNode.Parse(_archive.ExportWithPassages(Analyzed("Broken"), Set()).Content)!.AsObject();
        file["passageIndex"]!["passages"]![1]!["vector"] = "AACAPw==";
        await WriteAsync("broken.json", JsonSerializer.SerializeToUtf8Bytes(file));

        var listing = await new JsonExampleCatalog(_temp.Path).ListAsync(Token);

        listing.Books.ShouldBeEmpty();
        listing.Unreadable.ShouldHaveSingleItem().Reason.ShouldContain("passageIndex.passages[1].vector");
    }

    [Fact]
    public async Task AFileLargerThanABook_WithoutPassages_IsLeftOut()
    {
        var content = _archive.Export(Analyzed("Padded"));
        var padded = new byte[JsonBookRepository.MaxFileBytes + 1];
        Array.Fill(padded, (byte)' ');
        content.CopyTo(padded, 0);
        await WriteAsync("padded.json", padded);

        (await new JsonExampleCatalog(_temp.Path).ListAsync(Token)).Unreadable.ShouldHaveSingleItem().Reason.ShouldContain("too large");
    }
}
