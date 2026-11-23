namespace LivingPaper.Evals;

// The judge could not grade a reply. Code is a short stable word (invalid-verdict, content-filtered, model-error) and nothing else:
// never what the provider answered, which could repeat a key or an address.
internal sealed class JudgeException(string code) : Exception($"The judgement failed: {code}.")
{
    public string Code { get; } = code;
}
