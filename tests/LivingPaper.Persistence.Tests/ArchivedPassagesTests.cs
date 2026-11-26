using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// PAPER-88: the passages and vectors in the file of an exported book: how they are written, how large the file may be, and
// every way those of an imported file can be refused.
public class ArchivedPassagesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly JsonBookArchive _archive = new();

    private static Book Analyzed()
    {
        var book = new Book(BookId.New(), "Madame Bovary", "bovary.epub", Now) { Status = BookStatus.Done, Summary = "Normandy, 1840s.", UpdatedAt = Now };
        book.Characters.Add(new Character(1, "Emma", "A doctor's wife.", "I am Emma."));
        return book;
    }

    private static PassageSet Set(bool vectors = true) => new(
        [
            new StoredPassage(1, "« Elle savait valser, celle-là ! » 😀\nThe next line.", [new PassageScene("Emma waltzes.", ["Emma", "Viscount"]), new PassageScene("She looks out.", [])]),
            new StoredPassage(2, "Charles sleeps.", []),
        ],
        vectors ? "bge-m3" : null,
        vectors ? [[1f, 0f], [0.25f, -3.5f]] : []);

    private static PassageSet Many(int passages, int letters, int size) => new(
        [.. Enumerable.Range(1, passages).Select(number => new StoredPassage(number, new string('a', letters), []))],
        "bge-m3",
        [.. Enumerable.Range(1, passages).Select(_ => Enumerable.Repeat(0.5f, size).ToArray())]);

    private ArchiveParseResult RoundTrip(PassageSet set) => _archive.Parse(_archive.ExportWithPassages(Analyzed(), set).Content);

    private ArchiveParseResult Changed(Action<JsonObject> change)
    {
        var file = JsonNode.Parse(_archive.ExportWithPassages(Analyzed(), Set()).Content)!.AsObject();
        change(file["passageIndex"]!.AsObject());
        return _archive.Parse(JsonSerializer.SerializeToUtf8Bytes(file));
    }

    private static JsonObject Passage(JsonObject index, int at) => index["passages"]![at]!.AsObject();

    private static string Base64(params float[] numbers) => Convert.ToBase64String([.. numbers.SelectMany(BitConverter.GetBytes)]);

    // ---- The round trip ----

    [Fact]
    public void AnExportWithPassages_ReadBack_GivesTheSameBookPassagesAndVectors()
    {
        var book = Analyzed();
        var set = Set();

        var exported = _archive.ExportWithPassages(book, set);
        var result = _archive.Parse(exported.Content);

        exported.Omitted.ShouldBe(ExportOmission.None);
        result.Rejection.ShouldBeNull(result.Detail);
        result.Book!.ShouldBeEquivalentTo(book);
        result.Passages.ShouldNotBeNull();
        result.Passages.EmbeddingModel.ShouldBe("bge-m3");
        result.Passages.Passages.Select(passage => (passage.Number, passage.Text)).ShouldBe(set.Passages.Select(passage => (passage.Number, passage.Text)));
        result.Passages.Passages[0].Scenes.Select(scene => scene.Summary).ShouldBe(["Emma waltzes.", "She looks out."]);
        result.Passages.Passages[0].Scenes[0].Present.ShouldBe(["Emma", "Viscount"]);
        result.Passages.Passages[1].Scenes.ShouldBeEmpty();
        result.Passages.Vectors.Count.ShouldBe(2);
        result.Passages.Vectors[0].ShouldBe([1f, 0f]);
        result.Passages.Vectors[1].ShouldBe([0.25f, -3.5f]);
    }

    [Fact]
    public void TheVectorsAreWrittenAsBase64OfLittleEndianFloats_AndTheModelAndTheSizeOnce()
    {
        using var json = JsonDocument.Parse(_archive.ExportWithPassages(Analyzed(), Set()).Content);

        var index = json.RootElement.GetProperty("passageIndex");
        index.EnumerateObject().Select(property => property.Name).Order().ShouldBe(["model", "passages", "vectorSize"]);
        index.GetProperty("model").GetString().ShouldBe("bge-m3");
        index.GetProperty("vectorSize").GetInt32().ShouldBe(2);
        var first = index.GetProperty("passages")[0];
        first.EnumerateObject().Select(property => property.Name).Order().ShouldBe(["number", "scenes", "text", "vector"]);
        first.GetProperty("vector").GetString().ShouldBe("AACAPwAAAAA=");
        first.GetProperty("scenes")[0].EnumerateObject().Select(property => property.Name).Order().ShouldBe(["present", "summary"]);
        json.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(4);
    }

    [Fact]
    public void PassagesWithoutVectors_AreWrittenAndReadWithoutModelOrVector()
    {
        var exported = _archive.ExportWithPassages(Analyzed(), Set(vectors: false));

        var text = Encoding.UTF8.GetString(exported.Content);
        text.ShouldNotContain("\"vector");
        text.ShouldNotContain("\"model\": \"bge-m3\"");
        var read = _archive.Parse(exported.Content).Passages!;
        read.Passages.Count.ShouldBe(2);
        read.EmbeddingModel.ShouldBeNull();
        read.Vectors.ShouldBeEmpty();
    }

    [Fact]
    public void ASetWithNoPassage_AndAPlainExport_HoldNothingAboutPassages()
    {
        var empty = _archive.ExportWithPassages(Analyzed(), new PassageSet([], null, []));

        Encoding.UTF8.GetString(empty.Content).ShouldNotContain("passageIndex");
        empty.Omitted.ShouldBe(ExportOmission.None);
        _archive.Parse(empty.Content).Passages.ShouldBeNull();
        _archive.Parse(_archive.Export(Analyzed())).Passages.ShouldBeNull();
    }

    // ---- Sizes ----

    [Fact]
    public void AFileWithPassages_MayBeLargerThanABook()
    {
        var result = RoundTrip(Many(passages: 700, letters: 1900, size: 4));

        result.Rejection.ShouldBeNull(result.Detail);
        result.Passages!.Passages.Count.ShouldBe(700);
    }

    [Fact]
    public void AFileThatWouldBeTooLarge_LeavesTheVectorsOut_ThenThePassages()
    {
        var set = Many(passages: 20, letters: 500, size: 1000); // About 11 KB of text and 107 KB of vectors.

        var withoutVectors = new JsonBookArchive(maxBytes: 60_000).ExportWithPassages(Analyzed(), set);
        var withoutPassages = new JsonBookArchive(maxBytes: 5_000).ExportWithPassages(Analyzed(), set);

        withoutVectors.Omitted.ShouldBe(ExportOmission.Vectors);
        withoutVectors.Content.Length.ShouldBeLessThanOrEqualTo(60_000);
        var read = _archive.Parse(withoutVectors.Content).Passages!;
        read.Passages.Count.ShouldBe(20);
        read.Vectors.ShouldBeEmpty();
        withoutPassages.Omitted.ShouldBe(ExportOmission.Passages);
        _archive.Parse(withoutPassages.Content).Passages.ShouldBeNull();
        _archive.Parse(withoutPassages.Content).Book.ShouldNotBeNull();
    }

    [Fact]
    public void AFileOverTheLimit_IsRefused_BeforeBeingRead()
    {
        var huge = new byte[BookService.MaxImportBytes + 1];

        _archive.Parse(huge).Rejection.ShouldBe(ImportRejection.TooLarge);
        BookService.MaxImportBytes.ShouldBe(30L * 1024 * 1024);
    }

    [Fact]
    public void TheBookItself_StaysWithinItsOwnLimit_EvenInAFileWithPassages()
    {
        var content = _archive.ExportWithPassages(Analyzed(), Set()).Content;

        new JsonBookArchive(maxBookBytes: 200).Parse(content).Rejection.ShouldBe(ImportRejection.TooLarge);
    }

    // ---- Refusals ----

    [Fact]
    public void AFileWhosePassagesAreWrong_IsRefusedWhole()
    {
        var result = Changed(index => Passage(index, 1)["text"] = "");

        result.Book.ShouldBeNull();
        result.Passages.ShouldBeNull();
        result.Rejection.ShouldBe(ImportRejection.Invalid);
        result.Detail.ShouldBe("passageIndex.passages[1].text");
    }

    public static TheoryData<string, string> WrongPassages => new()
    {
        { "noPassage", "passageIndex.passages" },
        { "noList", "passageIndex.passages" },
        { "nullPassage", "passageIndex.passages[0]" },
        { "firstNumber", "passageIndex.passages[0].number" },
        { "sameNumber", "passageIndex.passages[1].number" },
        { "noText", "passageIndex.passages[0].text" },
        { "blankText", "passageIndex.passages[0].text" },
        { "longText", "passageIndex.passages[0].text" },
        { "noScenes", "passageIndex.passages[0].scenes" },
        { "tooManyScenes", "passageIndex.passages[0].scenes" },
        { "nullScene", "passageIndex.passages[0].scenes" },
        { "noSummary", "passageIndex.passages[0].scenes" },
        { "longSummary", "passageIndex.passages[0].scenes" },
        { "noPresent", "passageIndex.passages[0].scenes" },
        { "tooManyNames", "passageIndex.passages[0].scenes" },
        { "emptyName", "passageIndex.passages[0].scenes" },
        { "longName", "passageIndex.passages[0].scenes" },
        { "emptyModel", "passageIndex.model" },
        { "longModel", "passageIndex.model" },
        { "controlInModel", "passageIndex.model" },
        { "vectorsWithoutModel", "passageIndex.passages[0].vector" },
        { "sizeWithoutModel", "passageIndex.vectorSize" },
        { "noSize", "passageIndex.vectorSize" },
        { "zeroSize", "passageIndex.vectorSize" },
        { "hugeSize", "passageIndex.vectorSize" },
        { "missingVector", "passageIndex.passages[1].vector" },
        { "shortVector", "passageIndex.passages[1].vector" },
        { "longVector", "passageIndex.passages[1].vector" },
        { "notBase64", "passageIndex.passages[1].vector" },
        { "notANumber", "passageIndex.passages[1].vector" },
        { "infinite", "passageIndex.passages[1].vector" },
        { "allZeros", "passageIndex.passages[1].vector" },
    };

    private static void Break(string how, JsonObject index)
    {
        var first = Passage(index, 0);
        var second = Passage(index, 1);
        var scene = first["scenes"]![0]!.AsObject();
        JsonNode Scene() => new JsonObject { ["summary"] = "A scene.", ["present"] = new JsonArray() };
        switch (how)
        {
            case "noPassage": index["passages"] = new JsonArray(); break;
            case "noList": index.Remove("passages"); break;
            case "nullPassage": index["passages"]![0] = null; break;
            case "firstNumber": first["number"] = 2; break;
            case "sameNumber": second["number"] = 1; break;
            case "noText": first.Remove("text"); break;
            case "blankText": first["text"] = " \n "; break;
            case "longText": first["text"] = new string('a', PassageCutter.MaxLength + 1); break;
            case "noScenes": first.Remove("scenes"); break;
            case "tooManyScenes": first["scenes"] = new JsonArray([.. Enumerable.Range(0, PassageLimits.ScenesPerPassage + 1).Select(_ => Scene())]); break;
            case "nullScene": first["scenes"]![0] = null; break;
            case "noSummary": scene["summary"] = " "; break;
            case "longSummary": scene["summary"] = new string('a', PassageLimits.SceneLetters + 1); break;
            case "noPresent": scene.Remove("present"); break;
            case "tooManyNames": scene["present"] = new JsonArray([.. Enumerable.Range(0, PassageLimits.NamesPerScene + 1).Select(number => (JsonNode)("n" + number))]); break;
            case "emptyName": scene["present"] = new JsonArray(""); break;
            case "longName": scene["present"] = new JsonArray(new string('a', CharacterLimits.Name + 1)); break;
            case "emptyModel": index["model"] = " "; break;
            case "longModel": index["model"] = new string('m', LlmProviderLimits.Model + 1); break;
            case "controlInModel": index["model"] = "bge\nm3"; break;
            case "vectorsWithoutModel": index.Remove("model"); index.Remove("vectorSize"); break;
            case "sizeWithoutModel": index.Remove("model"); first.Remove("vector"); second.Remove("vector"); break;
            case "noSize": index.Remove("vectorSize"); break;
            case "zeroSize": index["vectorSize"] = 0; break;
            case "hugeSize": index["vectorSize"] = PassageLimits.VectorSize + 1; break;
            case "missingVector": second.Remove("vector"); break;
            case "shortVector": second["vector"] = Base64(1f); break;
            case "longVector": second["vector"] = Base64(1f, 2f, 3f); break;
            case "notBase64": second["vector"] = "not base64 !"; break;
            case "notANumber": second["vector"] = Base64(float.NaN, 1f); break;
            case "infinite": second["vector"] = Base64(float.PositiveInfinity, 1f); break;
            case "allZeros": second["vector"] = Base64(0f, 0f); break;
            default: throw new ArgumentOutOfRangeException(nameof(how), how, null);
        }
    }

    [Theory]
    [MemberData(nameof(WrongPassages))]
    public void EachWrongThing_IsRefused_AndOnlyTheFieldIsNamed(string how, string field)
    {
        var result = Changed(index => Break(how, index));

        result.Book.ShouldBeNull();
        result.Rejection.ShouldBe(ImportRejection.Invalid);
        result.Detail.ShouldBe(field);
    }

    [Fact]
    public void MorePassagesThanTheLimit_AreRefused()
    {
        var set = new PassageSet([.. Enumerable.Range(1, PassageLimits.Passages + 1).Select(number => new StoredPassage(number, "a", []))], null, []);

        RoundTrip(set).Detail.ShouldBe("passageIndex.passages");
    }

    [Fact]
    public void ASizeOfAnotherType_IsNotABook()
    {
        Changed(index => index["vectorSize"] = "two").Rejection.ShouldBe(ImportRejection.NotJson);
    }
}
