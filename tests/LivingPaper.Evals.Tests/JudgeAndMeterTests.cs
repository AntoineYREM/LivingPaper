using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Evals.Tests;

public class JudgeAndMeterTests
{
    private static readonly LlmProvider Provider = new("id", "Judge", "https://j.example/v1", "key", "model");
    private static readonly JudgePrompt Prompt = new("1", "character-fidelity", "{characterName}|{bookTitle}|{question}|{reply}");

    private static ModelJudge Judge(Func<string> answer, out FakeChatModel model)
    {
        model = new FakeChatModel(_ => answer());
        var captured = model;
        return new ModelJudge(new FakeChatModelFactory(_ => captured), Provider, Prompt, "Emma Bovary", "Madame Bovary");
    }

    [Fact]
    public async Task GradeAsync_SendsTheRenderedPromptAndReadsTheVerdict()
    {
        var judge = Judge(() => """{"score": 4, "reason": "ok"}""", out var model);

        var verdict = await judge.GradeAsync("Hi?", "Hello.", CancellationToken.None);

        verdict.ShouldBe(new JudgeVerdict(4, "ok"));
        model.Requests.Single().ShouldBe([new ChatMessage(ChatRole.User, "Emma Bovary|Madame Bovary|Hi?|Hello.")]);
    }

    [Fact]
    public async Task GradeAsync_FailsWithACodeWhenTheVerdictCannotBeRead()
    {
        var judge = Judge(() => "I refuse to grade.", out _);

        var error = await Should.ThrowAsync<JudgeException>(() => judge.GradeAsync("Hi?", "Hello.", CancellationToken.None));

        error.Code.ShouldBe("invalid-verdict");
        error.Message.ShouldNotContain("refuse");
    }

    private sealed class RefusingModel(Exception error) : IChatModel
    {
        public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
            throw error;

        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
            throw error;
    }

    [Fact]
    public async Task GradeAsync_TurnsTheFailuresOfTheModelIntoCodes()
    {
        var filtered = new ModelJudge(new FakeChatModelFactory(_ => new RefusingModel(new ContentFilteredException())), Provider, Prompt, "E", "B");
        var down = new ModelJudge(
            new FakeChatModelFactory(_ => new RefusingModel(new ChatProviderException(ChatFailureKind.Unreachable, "down"))), Provider, Prompt, "E", "B");

        (await Should.ThrowAsync<JudgeException>(() => filtered.GradeAsync("q", "r", CancellationToken.None))).Code.ShouldBe("content-filtered");
        (await Should.ThrowAsync<JudgeException>(() => down.GradeAsync("q", "r", CancellationToken.None))).Code.ShouldBe("model-error");
    }

    [Fact]
    public async Task MeteredFactory_HandsItsMeterToCallsThatAskForNone()
    {
        var meter = new ChatUsageMeter();
        var inner = new FakeChatModel(_ => "reply");
        var model = new MeteredChatModelFactory(new FakeChatModelFactory(_ => inner), meter).Create(Provider);

        await model.CompleteAsync([new ChatMessage(ChatRole.User, "hi")], usage: null, CancellationToken.None);
        await foreach (var unused in model.StreamReplyAsync([new ChatMessage(ChatRole.User, "hi")], usage: null, CancellationToken.None))
        {
            _ = unused;
        }

        // The model fills the meter it is given when the provider reports tokens.
        inner.Meters.ShouldBe([meter, meter]);
    }

    [Fact]
    public async Task MeteredFactory_KeepsTheMeterThatTheCallerGave()
    {
        var own = new ChatUsageMeter();
        var inner = new FakeChatModel(_ => "reply");
        var model = new MeteredChatModelFactory(new FakeChatModelFactory(_ => inner), new ChatUsageMeter()).Create(Provider);

        await model.CompleteAsync([new ChatMessage(ChatRole.User, "hi")], own, CancellationToken.None);

        inner.Meters.ShouldBe([own]);
    }
}
