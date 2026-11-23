using LivingPaper.Application;

namespace LivingPaper.Evals;

// Puts each question to the character, as many times as asked, and has each reply graded by the judge. One at a time: simple, and
// kind to the rate limits of the providers. A reply or a grade that fails is recorded and the run goes on; anything else is a bug
// and stops the run. Each try is traced as an item of the experiment when a tracer is given.
internal sealed class EvalRunner(ICharacterReplier replier, IJudge judge, IExperimentTracer? tracer = null)
{
    private readonly IExperimentTracer _tracer = tracer ?? NoExperimentTracer.Instance;

    public async Task<IReadOnlyList<EvalResult>> RunAsync(
        QuestionSet questions,
        int repeats,
        Action<EvalResult>? onResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentOutOfRangeException.ThrowIfLessThan(repeats, 1);

        var results = new List<EvalResult>();
        foreach (var question in questions.Items)
        {
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await RunOneAsync(question, repeat, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                onResult?.Invoke(result);
            }
        }

        return results;
    }

    private async Task<EvalResult> RunOneAsync(EvalQuestion question, int repeat, CancellationToken cancellationToken)
    {
        using var item = _tracer.BeginItem(question, repeat);

        string reply;
        try
        {
            reply = await replier.ReplyAsync(question.Input, cancellationToken).ConfigureAwait(false);
        }
        catch (ChatReplyException exception)
        {
            var failure = $"reply:{exception.Failure}";
            item.Complete(null, null, failure);
            return Result(question, repeat, item, reply: null, score: null, reason: null, failure);
        }

        try
        {
            var verdict = await judge.GradeAsync(question.Input, reply, cancellationToken).ConfigureAwait(false);
            item.Complete(reply, verdict.Score, null);
            return Result(question, repeat, item, reply, verdict.Score, verdict.Reason, failure: null);
        }
        catch (JudgeException exception)
        {
            var failure = $"judge:{exception.Code}";
            item.Complete(reply, null, failure);
            return Result(question, repeat, item, reply, score: null, reason: null, failure);
        }
    }

    private static EvalResult Result(EvalQuestion question, int repeat, IExperimentItem item, string? reply, int? score, string? reason, string? failure) =>
        new(question.Id, question.Category, repeat, score, reason, failure, question.Input, reply, item.TraceId, item.SpanId);
}
