using System.Text.Json;
using System.Text.RegularExpressions;

namespace LivingPaper.Evals;

// The prompt of a judge (evals/judge.json for the role, evals/accuracy-judge.json for the facts), with its version and the name of
// the score it produces. The judge is told who the character is, what the user wrote and what the character answered, and the expected
// answer when its template asks for it; it is never given the prompt that the character received.
internal sealed partial record JudgePrompt(string Version, string ScoreName, string Template)
{
    public const long MaxFileBytes = 262_144;

    private static readonly string[] Variables = ["characterName", "bookTitle", "question", "reply"];

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private const string ExpectedAnswer = "{expectedAnswer}";

    // A judge that grades against an expected answer: every question must then give one.
    public bool NeedsExpectedAnswer => Template.Contains(ExpectedAnswer, StringComparison.Ordinal);

    public static async Task<JudgePrompt> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            throw new EvalException($"The judge file {name} does not exist.");
        }

        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new EvalException($"The judge file {name} is too large.");
        }

        FileContent? file;
        try
        {
            await using var stream = File.OpenRead(path);
            file = await JsonSerializer.DeserializeAsync<FileContent>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new EvalException($"The judge file {name} is not valid JSON.");
        }

        if (file is null || file.SchemaVersion != 1)
        {
            throw new EvalException($"The judge file {name} must have \"schemaVersion\": 1.");
        }

        if (string.IsNullOrWhiteSpace(file.Version) || string.IsNullOrWhiteSpace(file.ScoreName) || string.IsNullOrWhiteSpace(file.Template))
        {
            throw new EvalException($"The judge file {name} needs a version, a scoreName and a template.");
        }

        var missing = Variables.Where(variable => !file.Template.Contains("{" + variable + "}", StringComparison.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            throw new EvalException($"The template of the judge file {name} must use {string.Join(", ", missing.Select(variable => "{" + variable + "}"))}.");
        }

        return new JudgePrompt(file.Version.Trim(), file.ScoreName.Trim(), file.Template);
    }

    // One pass over the template: a placeholder written inside a reply is text to grade, it is not filled in again.
    public string Render(string characterName, string bookTitle, EvalQuestion question, string reply)
    {
        ArgumentNullException.ThrowIfNull(question);
        var asked = question.Input;
        if (question.Conversation is { } turns)
        {
            var earlier = string.Join('\n', turns.Select(turn => $"{(turn.FromCharacter ? characterName : "User")}: {turn.Content}"));
            asked = $"Earlier in the conversation:\n{earlier}\n\nNow the user asks:\n{question.Input}";
        }

        return Render(characterName, bookTitle, asked, reply, question.ExpectedOutput);
    }

    public string Render(string characterName, string bookTitle, string question, string reply, string expectedAnswer = "")
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["characterName"] = characterName,
            ["bookTitle"] = bookTitle,
            ["question"] = question,
            ["reply"] = reply,
            ["expectedAnswer"] = expectedAnswer,
        };
        return VariablePattern().Replace(Template, match => values[match.Groups[1].Value]);
    }

    [GeneratedRegex(@"\{(characterName|bookTitle|question|reply|expectedAnswer)\}")]
    private static partial Regex VariablePattern();

    private sealed record FileContent(int SchemaVersion, string? Version, string? ScoreName, string? Template);
}
