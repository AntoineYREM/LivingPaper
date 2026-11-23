using LivingPaper.Application;
using Shouldly;

namespace LivingPaper.Evals.Tests;

public class EvalRunnerTests
{
    private static readonly QuestionSet Questions = new(
        "d",
        "madame-bovary",
        "Emma Bovary",
        [new EvalQuestion("q-1", "normal", "Hello?", "x"), new EvalQuestion("q-2", "trap", "Are you an AI?", "x")]);

    private sealed class Replier(Func<string, string> reply) : ICharacterReplier
    {
        public List<string> Asked { get; } = [];

        public Task<string> ReplyAsync(string question, CancellationToken cancellationToken)
        {
            Asked.Add(question);
            return Task.FromResult(reply(question));
        }
    }

    private sealed class Judge(Func<string, string, JudgeVerdict> grade) : IJudge
    {
        public Task<JudgeVerdict> GradeAsync(string question, string reply, CancellationToken cancellationToken) =>
            Task.FromResult(grade(question, reply));
    }

    [Fact]
    public async Task RunAsync_AsksEachQuestionOnceForEachRepeatAndGradesTheReply()
    {
        var replier = new Replier(question => "answer to " + question);
        var judge = new Judge((question, reply) => new JudgeVerdict(reply == "answer to Hello?" ? 5 : 2, "reason"));

        var results = await new EvalRunner(replier, judge).RunAsync(Questions, repeats: 2, onResult: null, CancellationToken.None);

        replier.Asked.ShouldBe(["Hello?", "Hello?", "Are you an AI?", "Are you an AI?"]);
        results.Count.ShouldBe(4);
        results.Select(result => (result.QuestionId, result.Repeat, result.Score)).ShouldBe(
            [("q-1", 1, 5), ("q-1", 2, 5), ("q-2", 1, 2), ("q-2", 2, 2)]);
        results[0].Category.ShouldBe("normal");
        results[0].Reason.ShouldBe("reason");
        results[0].Failure.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_ReportsEachResultAsItComes()
    {
        var seen = new List<string>();

        await new EvalRunner(new Replier(_ => "r"), new Judge((_, _) => new JudgeVerdict(3, string.Empty)))
            .RunAsync(Questions, 1, result => seen.Add(result.QuestionId), CancellationToken.None);

        seen.ShouldBe(["q-1", "q-2"]);
    }

    private sealed class FailingReplier : ICharacterReplier
    {
        public Task<string> ReplyAsync(string question, CancellationToken cancellationToken) =>
            question == "Hello?" ? throw new ChatReplyException(ChatFailure.ModelUnreachable) : Task.FromResult("fine");
    }

    [Fact]
    public async Task RunAsync_RecordsAFailedReplyAndGoesOn()
    {
        var results = await new EvalRunner(new FailingReplier(), new Judge((_, _) => new JudgeVerdict(4, string.Empty)))
            .RunAsync(Questions, 1, null, CancellationToken.None);

        results[0].Score.ShouldBeNull();
        results[0].Failure.ShouldBe("reply:ModelUnreachable");
        results[1].Score.ShouldBe(4);
    }

    private sealed class FailingJudge : IJudge
    {
        public Task<JudgeVerdict> GradeAsync(string question, string reply, CancellationToken cancellationToken) =>
            throw new JudgeException("invalid-verdict");
    }

    private sealed class JudgeWithAnUnreadableAnswer : IJudge
    {
        public Task<JudgeVerdict> GradeAsync(string question, string reply, CancellationToken cancellationToken) =>
            throw new JudgeException("invalid-verdict", "What the judge wrote.");
    }

    [Fact]
    public async Task RunAsync_KeepsWhatTheJudgeWroteWhenItCannotBeRead()
    {
        var results = await new EvalRunner(new Replier(_ => "r"), new JudgeWithAnUnreadableAnswer()).RunAsync(Questions, 1, null, CancellationToken.None);

        results.ShouldAllBe(result => result.Failure == "judge:invalid-verdict" && result.JudgeAnswer == "What the judge wrote." && result.Reply == "r");
    }

    [Fact]
    public async Task RunAsync_RecordsAFailedJudgementAndGoesOn()
    {
        var results = await new EvalRunner(new Replier(_ => "r"), new FailingJudge()).RunAsync(Questions, 1, null, CancellationToken.None);

        results.ShouldAllBe(result => result.Score == null && result.Failure == "judge:invalid-verdict");
    }

    private sealed class RecordingTracer : IExperimentTracer
    {
        public List<string> Log { get; } = [];

        public IExperimentItem BeginItem(EvalQuestion question, int repeat) => new Item(this, $"{question.Id}/{repeat}");

        private sealed class Item(RecordingTracer owner, string name) : IExperimentItem
        {
            public string? TraceId => "trace-" + name;

            public string? SpanId => "span-" + name;

            public void Complete(string? reply, int? score, string? failure) => owner.Log.Add($"{name} reply={reply} score={score} failure={failure}");

            public void Dispose() => owner.Log.Add("end " + name);
        }
    }

    [Fact]
    public async Task RunAsync_KeepsTheQuestionTheReplyAndTheTraceOfEachTry()
    {
        var tracer = new RecordingTracer();

        var results = await new EvalRunner(new Replier(question => "answer to " + question), new Judge((_, _) => new JudgeVerdict(4, "r")), tracer)
            .RunAsync(Questions, 1, null, CancellationToken.None);

        results[0].Input.ShouldBe("Hello?");
        results[0].Reply.ShouldBe("answer to Hello?");
        results[0].TraceId.ShouldBe("trace-q-1/1");
        results[0].SpanId.ShouldBe("span-q-1/1");
        tracer.Log.ShouldBe(
            [
                "q-1/1 reply=answer to Hello? score=4 failure=",
                "end q-1/1",
                "q-2/1 reply=answer to Are you an AI? score=4 failure=",
                "end q-2/1",
            ]);
    }

    [Fact]
    public async Task RunAsync_TellsTheTracerAboutAFailure()
    {
        var tracer = new RecordingTracer();

        await new EvalRunner(new FailingReplier(), new Judge((_, _) => new JudgeVerdict(4, "r")), tracer)
            .RunAsync(Questions, 1, null, CancellationToken.None);

        tracer.Log[0].ShouldBe("q-1/1 reply= score= failure=reply:ModelUnreachable");
    }

    [Fact]
    public async Task RunAsync_StopsWhenCancelled()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var act = () => new EvalRunner(new Replier(_ => "r"), new Judge((_, _) => new JudgeVerdict(3, string.Empty))).RunAsync(Questions, 1, null, source.Token);

        await Should.ThrowAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task RunAsync_RefusesLessThanOneRepeat()
    {
        var act = () => new EvalRunner(new Replier(_ => "r"), new Judge((_, _) => new JudgeVerdict(3, string.Empty))).RunAsync(Questions, 0, null, CancellationToken.None);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(act);
    }
}
