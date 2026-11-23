namespace LivingPaper.Evals;

// The judge could not grade a reply. Code is a short stable word (invalid-verdict, content-filtered, model-error) and nothing else:
// never what the provider answered, which could repeat a key or an address. The message never holds the answer of the judge.
// RawAnswer is set only for invalid-verdict: what the judge wrote instead of a grade, cut short, for the run file, so a grade that could
// not be read can be understood. It is never put in the message or in a log.
internal sealed class JudgeException(string code, string? rawAnswer = null) : Exception($"The judgement failed: {code}.")
{
    public const int MaxRawAnswerLength = 2000;

    public string Code { get; } = code;

    public string? RawAnswer { get; } = rawAnswer is { Length: > MaxRawAnswerLength } ? rawAnswer[..MaxRawAnswerLength] : rawAnswer;
}
