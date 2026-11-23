using LivingPaper.Persistence;

namespace LivingPaper.Evals;

// The command line of `charts`: where to write the pictures (default docs/images/evals of the repository).
internal sealed record ChartsOptions(bool ShowHelp, string? OutFolder)
{
    public const string Usage = """
        Usage: dotnet run --project tools/LivingPaper.Evals -- charts [--out <folder>]

        Draws the charts of the README from the published runs (evals/published/), their times (evals/published/latency.json) and the prices
        (evals/prices.json): the grade before and after the change of prompt, the grade against the price, and the time to answer. Each chart is
        written twice, as an SVG for a light page and for a dark one, so GitHub shows the one that matches its theme.

        --out <folder>   where to write them (default docs/images/evals)
        --help           this text

        The "before" and "after" runs are those made with the chat prompt of evals/prompts/baseline.json and with the current default prompt
        (src/LivingPaper.Persistence/prompts.default.json), told apart by the signature kept in each run file.
        """;

    public static ChartsOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            return new ChartsOptions(true, null);
        }

        string? folder = null;
        for (var index = 1; index < args.Count; index++)
        {
            if (args[index] == "--out")
            {
                if (index + 1 >= args.Count)
                {
                    throw new EvalException("The option --out needs a value.");
                }

                if (folder is not null)
                {
                    throw new EvalException("The option --out is given twice.");
                }

                folder = args[++index];
            }
            else
            {
                throw new EvalException($"Unknown argument '{args[index]}'. Use charts --help to see the options.");
            }
        }

        return new ChartsOptions(false, folder);
    }
}

// The `charts` command: reads the files that are kept in the repository (nothing is sent anywhere, Langfuse is not needed), works out the
// figures, and writes the six pictures.
internal static class ChartsCommand
{
    public static async Task<int> RunAsync(ChartsOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        var evals = RepoFiles.Find("evals") ?? throw new EvalException("The evals folder was not found: run the tool from the repository.");
        var published = Path.Combine(evals, "published");
        var runs = new List<RunRecord>();
        foreach (var file in Directory.GetFiles(published, "*.json").Order(StringComparer.Ordinal).Where(file => Path.GetFileName(file) != "latency.json"))
        {
            runs.Add(await RunStore.LoadAsync(file, cancellationToken).ConfigureAwait(false));
        }

        var latencies = await LatencyFile.ReadAsync(Path.Combine(published, "latency.json"), cancellationToken).ConfigureAwait(false);
        var prices = await PriceList.ReadAsync(Path.Combine(evals, "prices.json"), cancellationToken).ConfigureAwait(false);
        var baseline = await FingerprintAsync(Path.Combine(evals, "prompts", "baseline.json"), cancellationToken).ConfigureAwait(false);
        var defaults = RepoFiles.Find("src/LivingPaper.Persistence/prompts.default.json") ?? throw new EvalException("prompts.default.json was not found: run the tool from the repository.");
        var current = await FingerprintAsync(defaults, cancellationToken).ConfigureAwait(false);

        var data = ChartData.Build(runs, latencies, prices, baseline, current);
        foreach (var note in data.Notes)
        {
            output.WriteLine($"Note: {note}");
        }

        if (data.Quality.Count + data.Value.Count + data.Latency.Count == 0)
        {
            output.WriteLine("Nothing to draw.");
            return 1;
        }

        var folder = options.OutFolder ?? Path.Combine(Path.GetDirectoryName(evals)!, "docs", "images", "evals");
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { ChartTheme.Light, ChartTheme.Dark })
        {
            await WriteAsync(folder, $"quality-{theme.Name}.svg", ChartSvg.Quality(data.Quality, theme), output, cancellationToken).ConfigureAwait(false);
            await WriteAsync(folder, $"value-{theme.Name}.svg", ChartSvg.Value(data.Value, data.Currency, theme), output, cancellationToken).ConfigureAwait(false);
            await WriteAsync(folder, $"latency-{theme.Name}.svg", ChartSvg.Latency(data.Latency, theme), output, cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<string> FingerprintAsync(string promptsFile, CancellationToken cancellationToken)
    {
        try
        {
            return await new FilePromptProvider(Path.GetDirectoryName(promptsFile)!, promptsFile).ChatFingerprintAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (LivingPaper.Application.PromptFileException exception)
        {
            throw new EvalException(exception.Message);
        }
    }

    private static async Task WriteAsync(string folder, string name, string svg, TextWriter output, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(folder, name), svg, cancellationToken).ConfigureAwait(false);
        output.WriteLine($"Written: {Path.Combine(folder, name)}");
    }
}
