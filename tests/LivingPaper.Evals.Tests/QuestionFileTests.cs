using LivingPaper.Application;
using Shouldly;

namespace LivingPaper.Evals.Tests;

public class QuestionFileTests
{
    private const string Valid = """
        {
          "schemaVersion": 1, "dataset": "madame-bovary-emma", "bookId": "madame-bovary", "character": "Emma Bovary",
          "items": [
            { "id": "a-1", "category": "normal", "input": "Hello?", "expectedOutput": "Stays Emma." },
            { "id": "a-2", "category": "trap", "input": "Are you an AI?", "expectedOutput": "Stays Emma." }
          ]
        }
        """;

    private static async Task<QuestionSet> ReadAsync(string json)
    {
        using var folder = new TempFolder();
        return await QuestionFile.ReadAsync(folder.Write("questions.json", json), CancellationToken.None);
    }

    [Fact]
    public async Task ReadAsync_ReadsTheQuestionsInOrder()
    {
        var set = await ReadAsync(Valid);

        set.Dataset.ShouldBe("madame-bovary-emma");
        set.BookId.ShouldBe("madame-bovary");
        set.Character.ShouldBe("Emma Bovary");
        set.Items.Select(item => item.Id).ShouldBe(["a-1", "a-2"]);
        set.Items[1].Category.ShouldBe("trap");
        set.Items[1].Input.ShouldBe("Are you an AI?");
    }

    [Fact]
    public async Task ReadAsync_ReadsTheRealFileOfTheRepository()
    {
        var path = RepoFiles.Find("evals/bovary/questions.json");

        var set = await QuestionFile.ReadAsync(path!, CancellationToken.None);

        set.Items.Count.ShouldBe(20);
        set.Items.Select(item => item.Category).Distinct().Count().ShouldBe(5);

        // The language questions are in other languages than English, to check that the character answers in the language of the user.
        set.Items.Where(item => item.Category == "language").Select(item => item.Id).ShouldBe(["bovary-language-01", "bovary-language-02"]);
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 2")]
    [InlineData("\"dataset\": \"madame-bovary-emma\",", "")]
    [InlineData("\"bookId\": \"madame-bovary\",", "\"bookId\": \"Not A Slug\",")]
    [InlineData("\"id\": \"a-2\"", "\"id\": \"a-1\"")]
    [InlineData("\"input\": \"Hello?\"", "\"input\": \"\"")]
    [InlineData("\"category\": \"trap\"", "\"category\": \"  \"")]
    public async Task ReadAsync_RefusesAFileThatBreaksTheRules(string from, string to)
    {
        var act = () => ReadAsync(Valid.Replace(from, to, StringComparison.Ordinal));

        await Should.ThrowAsync<EvalException>(act);
    }

    [Fact]
    public async Task ReadAsync_RefusesAnEmptyListAndAMessageThatIsTooLong()
    {
        await Should.ThrowAsync<EvalException>(() => ReadAsync("""{ "schemaVersion": 1, "dataset": "d", "bookId": "b", "character": "C", "items": [] }"""));

        var tooLong = new string('x', ConversationLimits.MaxMessageLength + 1);
        await Should.ThrowAsync<EvalException>(() => ReadAsync(Valid.Replace("Hello?", tooLong, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ReadAsync_RefusesNotJsonAndAMissingFile()
    {
        await Should.ThrowAsync<EvalException>(() => ReadAsync("not json"));
        await Should.ThrowAsync<EvalException>(() => QuestionFile.ReadAsync(Path.Combine(Path.GetTempPath(), "no-such-questions.json"), CancellationToken.None));
    }
}
