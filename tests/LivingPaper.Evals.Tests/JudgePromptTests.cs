using Shouldly;

namespace LivingPaper.Evals.Tests;

public class JudgePromptTests
{
    private static JudgePrompt Prompt(string template) => new("1", "character-fidelity", template);

    [Fact]
    public void Render_FillsTheFourVariables()
    {
        var prompt = Prompt("{characterName} / {bookTitle} / {question} / {reply}");

        prompt.Render("Emma", "Madame Bovary", "Hi?", "Hello.").ShouldBe("Emma / Madame Bovary / Hi? / Hello.");
    }

    [Fact]
    public void Render_DoesNotFillAgainWhatAReplyContains()
    {
        var prompt = Prompt("{question} | {reply}");

        // A reply that holds a placeholder must stay as written: it is text to grade, not part of the prompt.
        prompt.Render("Emma", "B", "Q", "I said {question} and {characterName}").ShouldBe("Q | I said {question} and {characterName}");
    }

    [Fact]
    public void Render_KeepsTheBracesOfTheAnswerFormat()
    {
        var prompt = Prompt("{characterName} {bookTitle} {question} {reply} Answer: {\"score\": 3}");

        prompt.Render("E", "B", "Q", "R").ShouldEndWith("{\"score\": 3}");
    }

    [Fact]
    public async Task ReadAsync_ReadsTheRealJudgeOfTheRepository()
    {
        var prompt = await JudgePrompt.ReadAsync(RepoFiles.Find("evals/judge.json")!, CancellationToken.None);

        prompt.Version.ShouldBe("1");
        prompt.ScoreName.ShouldBe("character-fidelity");
        prompt.Render("Emma", "Madame Bovary", "Q", "R").ShouldNotContain("{reply}");
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "version": "1", "scoreName": "s", "template": "no variables here" }""")]
    [InlineData("""{ "schemaVersion": 1, "version": "", "scoreName": "s", "template": "{characterName}{bookTitle}{question}{reply}" }""")]
    [InlineData("""{ "schemaVersion": 2, "version": "1", "scoreName": "s", "template": "{characterName}{bookTitle}{question}{reply}" }""")]
    [InlineData("not json")]
    public async Task ReadAsync_RefusesAFileThatBreaksTheRules(string json)
    {
        using var folder = new TempFolder();

        await Should.ThrowAsync<EvalException>(() => JudgePrompt.ReadAsync(folder.Write("judge.json", json), CancellationToken.None));
    }
}
