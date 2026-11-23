namespace LivingPaper.Evals;

internal sealed record RunModel(string Provider, string Model);

internal sealed record RunJudge(string Provider, string Model, string PromptVersion, string ScoreName);

internal sealed record RunTokens(TokenUsage Chat, TokenUsage Judge);

// A whole run, as it is kept in a file (evals/runs/<id>.json): who answered and who judged, the tokens, and every try with the text of
// the question and of the reply. It holds no key and no address: the providers are named by the name given on the Settings page.
// Label is the short name the person gave the run, PromptsHash the signature of the chat prompts used (both null in older files): what
// tells two runs of the same model apart when a prompt was changed in between.
// The schema has a version, so older files stay readable when the format grows.
internal sealed record RunRecord(
    int SchemaVersion,
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string Dataset,
    string BookId,
    string Character,
    int Repeats,
    RunModel Chat,
    RunJudge Judge,
    RunTokens Tokens,
    IReadOnlyList<EvalResult> Results,
    string? Label = null,
    string? PromptsHash = null)
{
    public const int CurrentSchemaVersion = 1;
}
