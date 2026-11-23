namespace LivingPaper.Evals;

// The command line of `compare`: two runs (a path, or the id of a run kept in evals/runs/ or evals/published/) and an optional price file.
internal sealed record CompareOptions(bool ShowHelp, string RunA, string RunB, string? PricesPath)
{
    public const string Usage = """
        Usage: dotnet run --project tools/LivingPaper.Evals -- compare <run A> <run B> [--prices <file>]

        Compares two runs made on the same questions: the average by category, the questions that differ most, the tokens and the cost.
        B is read as the candidate and A as the baseline: a positive difference means B did better.

        A run is the path of a run file, or its id (the name of the file in evals/runs/ or evals/published/, with or without .json).
        --prices <file>   another price file than evals/prices.json (USD per million tokens, by model)
        --help            this text
        """;

    // Exactly two runs, anywhere among the options. Anything else is an error that says so.
    public static CompareOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count < 2 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            return new CompareOptions(true, string.Empty, string.Empty, null);
        }

        var runs = new List<string>();
        string? prices = null;
        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument == "--prices")
            {
                if (index + 1 >= args.Count)
                {
                    throw new EvalException("The option --prices needs a value.");
                }

                if (prices is not null)
                {
                    throw new EvalException("The option --prices is given twice.");
                }

                prices = args[++index];
            }
            else if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new EvalException($"Unknown option '{argument}'. Use compare --help to see the options.");
            }
            else
            {
                runs.Add(argument);
            }
        }

        return runs.Count == 2
            ? new CompareOptions(false, runs[0], runs[1], prices)
            : throw new EvalException($"compare needs exactly two runs, and {runs.Count} were given.");
    }
}
