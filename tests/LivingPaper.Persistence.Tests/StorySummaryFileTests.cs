using System.Text;
using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// PAPER-54: the story of the book, scene by scene, in the file of a book (schemaVersion 2) and in an export, and the files of
// version 1, which have none, that are still read.
public sealed class StorySummaryFileTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private const string Story = "Scene 1. Argan refuses to pay the doctor. Present: Argan, Toinette.\nScene 2. Toinette disguises herself. Present: Toinette, Argan.";

    private readonly TempDirectory _temp = new();
    private readonly JsonBookRepository _repository;
    private readonly JsonBookArchive _archive = new();

    public StorySummaryFileTests() => _repository = new JsonBookRepository(_temp.BooksPath);

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Book Analyzed(string? story = Story, bool partial = false)
    {
        var book = new Book(BookId.New(), "Le Malade imaginaire", "malade.pdf", Now)
        {
            Status = BookStatus.Done,
            Summary = "France, 1673.",
            StorySummary = story,
            StorySummaryPartial = partial,
            UpdatedAt = Now,
        };
        book.Characters.Add(new Character(1, "Argan", "A hypochondriac.", "I am Argan."));
        return book;
    }

    private static string StoryOf(string json) => JsonDocument.Parse(json).RootElement.GetProperty("storySummary").GetString()!;

    // ---- The file of the library ----

    [Fact]
    public async Task TheStory_AndItsHoles_AreKeptByTheLibrary()
    {
        var book = Analyzed(partial: true);

        await _repository.SaveAsync(book, Token);
        var loaded = await _repository.GetAsync(book.Id, Token);

        loaded.ShouldNotBeNull();
        loaded.StorySummary.ShouldBe(Story);
        loaded.StorySummaryPartial.ShouldBeTrue();
    }

    [Fact]
    public async Task ABookWithoutAStory_WritesNoStoryField_AndNoHoleField()
    {
        var book = Analyzed(story: null);

        await _repository.SaveAsync(book, Token);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_temp.BooksPath, book.Id + ".json"), Token));
        json.RootElement.TryGetProperty("storySummary", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("storySummaryPartial", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task AFileOfVersion1_WithoutStory_IsStillRead()
    {
        var book = Analyzed(story: null);
        await _repository.SaveAsync(book, Token);
        var path = Path.Combine(_temp.BooksPath, book.Id + ".json");
        var old = (await File.ReadAllTextAsync(path, Token)).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, old, Token);

        var loaded = await _repository.GetAsync(book.Id, Token);

        loaded.ShouldNotBeNull();
        loaded.Title.ShouldBe("Le Malade imaginaire");
        loaded.StorySummary.ShouldBeNull();
        loaded.StorySummaryPartial.ShouldBeFalse();
    }

    [Fact]
    public async Task AFileOfANewerVersion_IsRefusedWithItsReason_NotReadAsABook()
    {
        var book = Analyzed();
        await _repository.SaveAsync(book, Token);
        var path = Path.Combine(_temp.BooksPath, book.Id + ".json");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path, Token)).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 3", StringComparison.Ordinal), Token);

        (await Should.ThrowAsync<BookUnreadableException>(() => _repository.GetAsync(book.Id, Token))).Message.ShouldContain("schemaVersion 3");
    }

    // ---- The export and the import ----

    [Fact]
    public void AnExport_HoldsTheStory_AndReadBackGivesTheSameBook()
    {
        var book = Analyzed(partial: true);

        var bytes = _archive.Export(book);
        var result = _archive.Parse(bytes);

        StoryOf(Encoding.UTF8.GetString(bytes)).ShouldBe(Story);
        result.Rejection.ShouldBeNull();
        result.Book.ShouldBeEquivalentTo(book);
    }

    [Fact]
    public void AnImportOfVersion1_WithoutStory_IsAccepted()
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(_archive.Export(Analyzed(story: null)))!;
        map["schemaVersion"] = 1;

        var result = _archive.Parse(JsonSerializer.SerializeToUtf8Bytes(map));

        result.Rejection.ShouldBeNull();
        result.Book!.StorySummary.ShouldBeNull();
    }

    [Fact]
    public void AStoryLongerThanAllowed_IsRefused_AsAnInvalidFile()
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(_archive.Export(Analyzed()))!;
        map["storySummary"] = new string('s', CharacterLimits.StorySummary + 1);

        var result = _archive.Parse(JsonSerializer.SerializeToUtf8Bytes(map));

        result.Rejection.ShouldBe(ImportRejection.Invalid);
        result.Detail!.ShouldContain("storySummary");
    }

    [Fact]
    public void AStoryOfTheAllowedLength_IsAccepted()
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(_archive.Export(Analyzed()))!;
        map["storySummary"] = new string('s', CharacterLimits.StorySummary);

        _archive.Parse(JsonSerializer.SerializeToUtf8Bytes(map)).Rejection.ShouldBeNull();
    }
}
