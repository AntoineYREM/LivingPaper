using System.Globalization;

namespace LivingPaper.Evals;

// The command line: `run`, with a few options. A model can also be named by an environment variable (EVAL_CHAT_PROVIDER,
// EVAL_JUDGE_PROVIDER, from the shell or .env), for a run with no keyboard.
internal sealed record EvalOptions(
    bool ShowHelp,
    int Repeats,
    string? QuestionsPath,
    string? JudgePromptPath,
    string? ChatProvider,
    string? JudgeProvider,
    bool LocalOnly = false,
    string? PromptsPath = null,
    string? Label = null,
    int? MaxOutputTokens = null)
{
    public const int DefaultRepeats = 3;
    public const int MaxRepeats = 20;
    public const int MaxLabelLength = 60;
    public const int MaxCeiling = 200_000;

    public const string Usage = """
        Usage: dotnet run --project tools/LivingPaper.Evals -- run [options]

        Puts the questions of evals/bovary/questions.json to the character through the real chat, has a judge grade each reply
        from 1 to 5, and prints the average by category.

        Options:
          --repeats <n>          times each question is asked (1 to 20, default 3)
          --chat <name>          language model of the character, by its name on the Settings page
          --judge <name>         language model of the judge, by its name on the Settings page
          --questions <file>     another questions file (default evals/bovary/questions.json)
          --judge-prompt <file>  another judge prompt (default evals/judge.json)
          --prompts-file <file>  another prompts file for the chat (default: prompts.json of the data folder), to try a change of prompt
          --label <text>         a short name for this run (for example "rule-no-ai"), kept in the run file and shown by compare
          --max-tokens <n>       ceiling on the length of every reply, thinking included (default: none). Useful with an expensive model on
                                 OpenRouter, which sets aside the cost of the longest reply first; not for OpenAI's own newest models
          --local-only           keep the run in evals/runs/ only, even when Langfuse is configured
          --help                 this text

        Every run is saved in evals/runs/ (not committed: it holds the replies). With the LANGFUSE_* variables set (shell or .env) it
        is also sent to Langfuse: the dataset, one trace per answer, and the grades as scores.

        Without --chat and --judge the tool shows the list of models and asks. In a script, name them here or with
        EVAL_CHAT_PROVIDER and EVAL_JUDGE_PROVIDER. The data folder is DATA_DIRECTORY (default ./data), the same as the site's.
        """;

    // The first argument must be "run" (or --help). A repeated, unknown or incomplete option is an error that says so.
    public static EvalOptions Parse(IReadOnlyList<string> args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        if (args.Count == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            return new EvalOptions(true, DefaultRepeats, null, null, null, null);
        }

        if (args[0] != "run")
        {
            throw new EvalException($"Unknown command '{args[0]}'. The only command is 'run'.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var localOnly = false;
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            if (option == "--local-only")
            {
                localOnly = true;
                index--;
                continue;
            }

            if (option is not ("--repeats" or "--chat" or "--judge" or "--questions" or "--judge-prompt" or "--prompts-file" or "--label" or "--max-tokens"))
            {
                throw new EvalException($"Unknown option '{option}'. Use --help to see the options.");
            }

            if (index + 1 >= args.Count)
            {
                throw new EvalException($"The option {option} needs a value.");
            }

            if (!values.TryAdd(option, args[index + 1]))
            {
                throw new EvalException($"The option {option} is given twice.");
            }
        }

        var repeats = DefaultRepeats;
        if (values.TryGetValue("--repeats", out var repeatsText)
            && (!int.TryParse(repeatsText, NumberStyles.None, CultureInfo.InvariantCulture, out repeats) || repeats is < 1 or > MaxRepeats))
        {
            throw new EvalException($"--repeats must be a whole number from 1 to {MaxRepeats}.");
        }

        var label = values.GetValueOrDefault("--label")?.Trim();
        if (label is not null && (label.Length is 0 or > MaxLabelLength || label.Any(char.IsControl)))
        {
            throw new EvalException($"--label must be a short text of 1 to {MaxLabelLength} characters.");
        }

        int? ceiling = null;
        if (values.TryGetValue("--max-tokens", out var ceilingText))
        {
            if (!int.TryParse(ceilingText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > MaxCeiling)
            {
                throw new EvalException($"--max-tokens must be a whole number from 1 to {MaxCeiling}.");
            }

            ceiling = parsed;
        }

        return new EvalOptions(
            ShowHelp: false,
            repeats,
            values.GetValueOrDefault("--questions"),
            values.GetValueOrDefault("--judge-prompt"),
            values.GetValueOrDefault("--chat") ?? Blank(environment("EVAL_CHAT_PROVIDER")),
            values.GetValueOrDefault("--judge") ?? Blank(environment("EVAL_JUDGE_PROVIDER")),
            localOnly,
            values.GetValueOrDefault("--prompts-file"),
            label,
            ceiling);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
