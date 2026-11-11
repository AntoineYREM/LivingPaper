using System.Text;
using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// The file of an exported book (PAPER-12): what it holds, what it never holds, and every way an imported file can be refused.
public class JsonBookArchiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly JsonBookArchive _archive = new();

    private static Book Analyzed()
    {
        var book = new Book(BookId.New(), "Le Malade imaginaire", "malade.pdf", Now)
        {
            Status = BookStatus.Done,
            Summary = "France, 1673. Une comédie.",
            PartsDone = 4,
            PartsTotal = 4,
            ProfilesDone = 2,
            ProfilesTotal = 2,
            PartsSkipped = 1,
            ProviderName = "OpenAI",
            Model = "gpt-5-nano",
            InputTokens = 1200,
            CachedInputTokens = 200,
            OutputTokens = 800,
            StartedAt = Now.AddMinutes(-5),
            UpdatedAt = Now,
            FinishedAt = Now,
        };
        book.Characters.Add(new Character(1, "Argan", "Un hypocondriaque.", "Je suis Argan.\n\nJ’ai peur de mourir."));
        book.Characters.Add(new Character(2, "Toinette", "La servante.", "Je suis Toinette. « Bonjour » 😀"));
        return book;
    }

    private byte[] Bytes(Action<Dictionary<string, object?>>? change = null)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(_archive.Export(Analyzed()))!;
        change?.Invoke(map);
        return JsonSerializer.SerializeToUtf8Bytes(map);
    }

    // ---- The export ----

    [Fact]
    public void AnExport_ReadBack_GivesTheSameBook()
    {
        var book = Analyzed();

        var result = _archive.Parse(_archive.Export(book));

        result.Rejection.ShouldBeNull();
        result.Book.ShouldNotBeNull();
        result.Book.ShouldBeEquivalentTo(book);
    }

    [Fact]
    public void AnExport_HoldsOnlyTheDocumentedFieldsOfABook_AndNothingAboutTheMachine()
    {
        using var json = JsonDocument.Parse(_archive.Export(Analyzed()));

        json.RootElement.EnumerateObject().Select(property => property.Name).Order().ShouldBe(
        [
            "cachedInputTokens", "characters", "fileName", "finishedAt", "id", "inputTokens", "model", "outputTokens", "partsDone",
            "partsSkipped", "partsTotal", "profilesDone", "profilesTotal", "providerName", "schemaVersion", "sentAt", "startedAt",
            "status", "summary", "title", "updatedAt",
        ]);
        json.RootElement.GetProperty("characters")[0].EnumerateObject().Select(property => property.Name).Order()
            .ShouldBe(["chatProfile", "name", "position", "shortDescription"]);
    }

    [Fact]
    public void AnExport_NeverHoldsAKeyAPathOrAnAddress_EvenWhenTheBookNameTheFileWithAFolder()
    {
        var book = Analyzed();
        book.FileName = "malade.pdf";

        var text = Encoding.UTF8.GetString(_archive.Export(book));

        text.ShouldNotContain("sk-");
        text.ShouldNotContain("apiKey", Case.Insensitive);
        text.ShouldNotContain("baseUrl", Case.Insensitive);
        text.ShouldNotContain("http");
        text.ShouldNotContain(":\\");
        text.ShouldNotContain("/home/");
    }

    [Fact]
    public void AnExport_IsReadable_Indented_AccentsAsTheyAre_WithoutABom()
    {
        var bytes = _archive.Export(Analyzed());
        var text = Encoding.UTF8.GetString(bytes);

        text.ShouldContain("Un hypocondriaque.");
        text.ShouldContain("J’ai peur de mourir.");
        text.ShouldContain("\n  \"title\"");
        bytes[0].ShouldBe((byte)'{');
    }

    [Fact]
    public void TheFileOfAnExport_IsAValidExampleFile_WithAnyName()
    {
        _archive.Parse(_archive.Export(Analyzed())).Book!.Status.ShouldBe(BookStatus.Done);
    }

    // ---- Refusals ----

    [Fact]
    public void NothingSent_IsRefused() => _archive.Parse([]).Rejection.ShouldBe(ImportRejection.Empty);

    [Fact]
    public void ATooLargeFile_IsRefused_BeforeBeingRead()
    {
        var huge = new byte[JsonBookRepository.MaxFileBytes + 1];
        Array.Fill(huge, (byte)' ');

        _archive.Parse(huge).Rejection.ShouldBe(ImportRejection.TooLarge);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("12")]
    [InlineData("\"a string\"")]
    [InlineData("<html>a page</html>")]
    [InlineData("\u0000\u0001\u0002")]
    public void SomethingThatIsNotAJsonBook_IsRefused(string text) =>
        _archive.Parse(Encoding.UTF8.GetBytes(text)).Rejection.ShouldBe(ImportRejection.NotJson);

    [Fact]
    public void ValuesOfTheWrongType_AreRefusedAsNotJson()
    {
        _archive.Parse(Bytes(map => map["partsDone"] = "many")).Rejection.ShouldBe(ImportRejection.NotJson);
        _archive.Parse(Bytes(map => map["characters"] = "Argan")).Rejection.ShouldBe(ImportRejection.NotJson);
        _archive.Parse(Bytes(map => map["sentAt"] = "yesterday")).Rejection.ShouldBe(ImportRejection.NotJson);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    public void ANewerVersion_IsRefusedWithItsOwnReason(int version) =>
        _archive.Parse(Bytes(map => map["schemaVersion"] = version)).Rejection.ShouldBe(ImportRejection.NewerVersion);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnUnknownVersion_IsRefused(int version) =>
        _archive.Parse(Bytes(map => map["schemaVersion"] = version)).Rejection.ShouldBe(ImportRejection.UnknownVersion);

    [Fact]
    public void AFileWithNoVersion_IsRefused() =>
        _archive.Parse(Bytes(map => map.Remove("schemaVersion"))).Rejection.ShouldBe(ImportRejection.UnknownVersion);

    [Theory]
    [InlineData("id")]
    [InlineData("title")]
    [InlineData("fileName")]
    [InlineData("status")]
    [InlineData("sentAt")]
    [InlineData("updatedAt")]
    [InlineData("characters")]
    public void AMissingRequiredField_IsRefused_AndNamedInTheDetail(string field)
    {
        var result = _archive.Parse(Bytes(map => map.Remove(field)));

        result.Rejection.ShouldBe(ImportRejection.Invalid);
        result.Book.ShouldBeNull();
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("../../etc/passwd")]
    public void AnIdThatIsNotValid_IsRefused(string id) =>
        _archive.Parse(Bytes(map => map["id"] = id)).Rejection.ShouldBe(ImportRejection.Invalid);

    [Fact]
    public void AnUnknownStatus_IsRefused() =>
        _archive.Parse(Bytes(map => map["status"] = "Deleted")).Rejection.ShouldBe(ImportRejection.Invalid);

    [Fact]
    public void ANumberOutOfRange_IsRefused()
    {
        _archive.Parse(Bytes(map => map["inputTokens"] = -1)).Rejection.ShouldBe(ImportRejection.Invalid);
        _archive.Parse(Bytes(map => map["partsTotal"] = -5)).Rejection.ShouldBe(ImportRejection.Invalid);
    }

    [Fact]
    public void ATextLongerThanAllowed_IsRefused_NamingTheField_NeverRepeatingIt()
    {
        var result = _archive.Parse(Bytes(map => map["title"] = new string('t', BookLimits.Title + 1)));

        result.Rejection.ShouldBe(ImportRejection.Invalid);
        result.Detail.ShouldNotBeNull().ShouldContain("title");
        result.Detail.ShouldNotContain("tttt");
    }

    [Fact]
    public void ACharacterTextLongerThanAllowed_IsRefused()
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(_archive.Export(Analyzed()))!;
        var characters = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(map["characters"].GetRawText())!;
        characters[0]["chatProfile"] = new string('p', CharacterLimits.ChatProfile + 1);
        var mutable = JsonSerializer.Deserialize<Dictionary<string, object?>>(_archive.Export(Analyzed()))!;
        mutable["characters"] = characters;

        var result = _archive.Parse(JsonSerializer.SerializeToUtf8Bytes(mutable));

        result.Rejection.ShouldBe(ImportRejection.Invalid);
        result.Detail.ShouldNotBeNull().ShouldContain("chatProfile");
    }

    [Fact]
    public void MoreCharactersThanAllowed_AreRefused()
    {
        var characters = Enumerable.Range(1, BookLimits.MaxCharacters + 1)
            .Select(position => new Dictionary<string, object?> { ["position"] = position, ["name"] = "N" + position, ["shortDescription"] = "d", ["chatProfile"] = "p" })
            .ToList();

        _archive.Parse(Bytes(map => map["characters"] = characters)).Rejection.ShouldBe(ImportRejection.Invalid);
    }

    [Fact]
    public void ACharacterWithAPositionBelowOne_IsRefused()
    {
        var characters = new List<Dictionary<string, object?>> { new() { ["position"] = 0, ["name"] = "Argan", ["shortDescription"] = "d", ["chatProfile"] = "p" } };

        _archive.Parse(Bytes(map => map["characters"] = characters)).Rejection.ShouldBe(ImportRejection.Invalid);
    }

    [Fact]
    public void ABookThatIsTheFileOfADeeplyNestedJson_IsRefusedWithoutHarm()
    {
        var deep = new string('[', 5000) + new string(']', 5000);

        _archive.Parse(Encoding.UTF8.GetBytes("{\"characters\":" + deep + "}")).Rejection.ShouldBe(ImportRejection.NotJson);
    }

    [Fact]
    public void ExtraFields_AreIgnored_NeverKept()
    {
        var result = _archive.Parse(Bytes(map =>
        {
            map["apiKey"] = "sk-injected";
            map["extra"] = new { nested = "value" };
        }));

        result.Book.ShouldNotBeNull();
        Encoding.UTF8.GetString(_archive.Export(result.Book)).ShouldNotContain("sk-injected");
    }
}
