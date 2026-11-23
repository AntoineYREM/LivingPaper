namespace LivingPaper.Evals;

// The command line of `latency`: one or more runs (a path, or the id of a run kept in evals/runs/ or evals/published/) and an optional file to write.
internal sealed record LatencyOptions(bool ShowHelp, IReadOnlyList<string> Runs, string? OutPath)
{
    public const string Usage = """
        Usage: dotnet run --project tools/LivingPaper.Evals -- latency <run> [<run> ...] [--out <file>]

        Shows how long the model of the chat took to answer in each run, as Langfuse recorded it: the mean, the median and the slowest tenth of
        the answers, and the mean time until the first text appeared. The judge is not counted. It needs the runs to have been sent to Langfuse
        (LANGFUSE_* in the shell or in .env), and reads from it only.

        A run is the path of a run file, or its id (the name of the file in evals/runs/ or evals/published/, with or without .json).
        --out <file>   also write the figures as JSON (for example evals/published/latency.json)
        --help         this text

        The figures depend on the network between you and the provider and on how busy the provider was: they are orders of magnitude, measured
        where and when the run was made.
        """;

    public static LatencyOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count < 2 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            return new LatencyOptions(true, [], null);
        }

        var runs = new List<string>();
        string? output = null;
        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument == "--out")
            {
                if (index + 1 >= args.Count)
                {
                    throw new EvalException("The option --out needs a value.");
                }

                if (output is not null)
                {
                    throw new EvalException("The option --out is given twice.");
                }

                output = args[++index];
            }
            else if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new EvalException($"Unknown option '{argument}'. Use latency --help to see the options.");
            }
            else
            {
                runs.Add(argument);
            }
        }

        return runs.Count > 0 ? new LatencyOptions(false, runs, output) : throw new EvalException("latency needs at least one run.");
    }
}
