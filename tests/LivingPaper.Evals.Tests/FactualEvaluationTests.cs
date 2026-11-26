using Shouldly;

namespace LivingPaper.Evals.Tests;

// PAPER-87: factual questions (an expected answer, the phrases of the book that hold it, an earlier conversation) and the judge
// that grades them against the expected answer.
public class FactualEvaluationTests
{
    private static async Task<QuestionSet> ReadItemAsync(string item)
    {
        var json = $$"""
            { "schemaVersion": 1, "dataset": "d", "bookId": "madame-bovary", "character": "Emma Bovary", "items": [ {{item}} ] }
            """;
        using var folder = new TempFolder();
        return await QuestionFile.ReadAsync(folder.Write("questions.json", json), CancellationToken.None);
    }

    [Fact]
    public async Task ReadAsync_ReadsThePhrasesOfTheBookAndTheEarlierConversation()
    {
        var set = await ReadItemAsync("""
            { "id": "f-1", "category": "follow-up", "input": "And then?", "expectedOutput": "To the cathedral.",
              "passageQuotes": [ " la cathédrale ", "le fiacre" ],
              "conversation": [ { "role": "user", "content": "Where did you meet Léon?" }, { "role": "assistant", "content": "In Rouen." } ] }
            """);

        var item = set.Items.Single();
        item.PassageQuotes.ShouldBe(["la cathédrale", "le fiacre"]);
        item.Conversation!.Select(turn => (turn.FromCharacter, turn.Content))
            .ShouldBe([(false, "Where did you meet Léon?"), (true, "In Rouen.")]);
    }

    [Fact]
    public async Task ReadAsync_AQuestionWithoutThemHasNone()
    {
        var item = (await ReadItemAsync("""{ "id": "f-1", "category": "fact", "input": "Why?" }""")).Items.Single();

        item.PassageQuotes.ShouldBeNull();
        item.Conversation.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{ "id": "f-1", "category": "c", "input": "q", "passageQuotes": [ " " ] }""")]
    [InlineData("""{ "id": "f-1", "category": "c", "input": "q", "passageQuotes": [] }""")]
    [InlineData("""{ "id": "f-1", "category": "c", "input": "q", "passageQuotes": [ null ] }""")]
    [InlineData("""{ "id": "f-1", "category": "c", "input": "q", "conversation": [ { "role": "system", "content": "x" } ] }""")]
    [InlineData("""{ "id": "f-1", "category": "c", "input": "q", "conversation": [ { "role": "user", "content": " " } ] }""")]
    [InlineData("""{ "id": "f-1", "category": "c", "input": "q", "conversation": [ { "role": "user", "content": "no reply after it" } ] }""")]
    [InlineData("""{ "id": "f-1", "category": "c", "input": "q", "conversation": [] }""")]
    public Task ReadAsync_RefusesPhrasesOrAConversationThatCannotBeUsed(string item) =>
        Should.ThrowAsync<EvalException>(() => ReadItemAsync(item));

    [Fact]
    public void Render_FillsTheExpectedAnswerAndTheEarlierConversation()
    {
        var prompt = new JudgePrompt("1", "accuracy", "{characterName}|{bookTitle}|{question}|{reply}|{expectedAnswer}");
        var question = new EvalQuestion("f-1", "follow-up", "And then?", "To the cathedral.")
        {
            Conversation = [new ConversationTurn(false, "Where?"), new ConversationTurn(true, "In Rouen.")],
        };

        prompt.NeedsExpectedAnswer.ShouldBeTrue();
        prompt.Render("Emma", "B", question, "R").ShouldBe(
            "Emma|B|Earlier in the conversation:\nUser: Where?\nEmma: In Rouen.\n\nNow the user asks:\nAnd then?|R|To the cathedral.");
    }

    [Fact]
    public void Render_ASingleQuestionIsWrittenAlone()
    {
        var prompt = new JudgePrompt("1", "s", "{question}");

        prompt.NeedsExpectedAnswer.ShouldBeFalse();
        prompt.Render("Emma", "B", new EvalQuestion("q", "c", "Hi?", "guide"), "R").ShouldBe("Hi?");
    }

    // The specification asks for at least 20 factual questions, each with its expected answer.
    [Fact]
    public async Task ReadAsync_ReadsTheFactualQuestionsOfTheRepository()
    {
        var set = await QuestionFile.ReadAsync(RepoFiles.Find("evals/bovary/factual.json")!, CancellationToken.None);

        set.Dataset.ShouldBe("madame-bovary-facts");
        set.Items.Count.ShouldBeGreaterThanOrEqualTo(20);
        set.Items.ShouldAllBe(item => item.ExpectedOutput.Length > 0 && item.PassageQuotes != null);
        set.Items.Where(item => item.Category == "follow-up").ShouldAllBe(item => item.Conversation != null);
        set.Items.Where(item => item.Category == "fact").ShouldAllBe(item => item.Conversation == null);
    }

    [Fact]
    public async Task ReadAsync_ReadsTheAccuracyJudgeOfTheRepository()
    {
        var prompt = await JudgePrompt.ReadAsync(RepoFiles.Find("evals/accuracy-judge.json")!, CancellationToken.None);

        prompt.ScoreName.ShouldBe("factual-accuracy");
        prompt.NeedsExpectedAnswer.ShouldBeTrue();
    }
}
