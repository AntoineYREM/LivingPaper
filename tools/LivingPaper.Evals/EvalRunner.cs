using LivingPaper.Application;

namespace LivingPaper.Evals;

// Puts each question to the character, as many times as asked, and has each reply graded by the judge. One at a time: simple, and
// kind to the rate limits of the providers. A reply or a grade that fails is recorded and the run goes on; anything else is a bug
// and stops the run.
internal sealed class EvalRunner(ICharacterReplier replier, IJudge judge)
{
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
        string reply;
        try
        {
            reply = await replier.ReplyAsync(question.Input, cancellationToken).ConfigureAwait(false);
        }
        catch (ChatReplyException exception)
        {
            return new EvalResult(question.Id, question.Category, repeat, null, null, $"reply:{exception.Failure}");
        }

        try
        {
            var verdict = await judge.GradeAsync(question.Input, reply, cancellationToken).ConfigureAwait(false);
            return new EvalResult(question.Id, question.Category, repeat, verdict.Score, verdict.Reason, null);
        }
        catch (JudgeException exception)
        {
            return new EvalResult(question.Id, question.Category, repeat, null, null, $"judge:{exception.Code}");
        }
    }
}
