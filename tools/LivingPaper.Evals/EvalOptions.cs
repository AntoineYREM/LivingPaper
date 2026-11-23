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
    string? JudgeProvider)
{
    public const int DefaultRepeats = 3;
    public const int MaxRepeats = 20;

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
          --help                 this text

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
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            if (option is not ("--repeats" or "--chat" or "--judge" or "--questions" or "--judge-prompt"))
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

        return new EvalOptions(
            ShowHelp: false,
            repeats,
            values.GetValueOrDefault("--questions"),
            values.GetValueOrDefault("--judge-prompt"),
            values.GetValueOrDefault("--chat") ?? Blank(environment("EVAL_CHAT_PROVIDER")),
            values.GetValueOrDefault("--judge") ?? Blank(environment("EVAL_JUDGE_PROVIDER")));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
