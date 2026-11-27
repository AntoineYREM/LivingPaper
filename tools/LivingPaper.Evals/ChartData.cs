namespace LivingPaper.Evals;

// A row of the chart "before and after": the average grade of a model with the first prompt and with the current one.
internal sealed record QualityRow(string Label, double Before, double After);

// A point of the chart "grade and price": the average grade of a model with the current prompt and what 1,000 replies of its chat cost
// (null when the model has no price, 0 for a free or local model).
internal sealed record ValuePoint(string Name, double Score, double? CostPer1000)
{
    public bool IsFree => CostPer1000 is 0;
}

// A bar of the chart "time to answer": the mean time of the chat call, and the mean time until its first text (null when not recorded).
internal sealed record LatencyBar(string Name, double TotalSeconds, double? FirstTextSeconds);

// What the charts of evals/README.md show, worked out from the published runs (evals/published/), the times (latency.json) and the prices.
// Nothing is typed in by hand: a figure on a chart is the one of the files.
internal sealed record ChartData(
    IReadOnlyList<QualityRow> Quality,
    IReadOnlyList<ValuePoint> Value,
    IReadOnlyList<LatencyBar> Latency,
    string Currency,
    IReadOnlyList<string> Notes)
{
    // baselineHash and currentHash are the signatures of the chat prompts before and after the change (RunRecord.PromptsHash). The main
    // dataset is the one on which the most models were run with the current prompt (Emma Bovary): the charts of the price and of the time
    // are about it, and the chart "before and after" adds the rows of the other datasets, named after their character.
    public static ChartData Build(
        IReadOnlyList<RunRecord> runs,
        IReadOnlyList<LatencyEntry> latencies,
        PriceList prices,
        string baselineHash,
        string currentHash)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(latencies);
        ArgumentNullException.ThrowIfNull(prices);

        var notes = new List<string>();
        var mainDataset = runs
            .Where(run => run.PromptsHash == currentHash)
            .GroupBy(run => run.Dataset, StringComparer.Ordinal)
            .OrderByDescending(group => group.Select(run => run.Chat.Model).Distinct(StringComparer.Ordinal).Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();
        if (mainDataset is null)
        {
            return new ChartData([], [], [], prices.Currency, ["No published run was made with the current chat prompt."]);
        }

        return new ChartData(
            QualityRows(runs, prices, baselineHash, currentHash, mainDataset),
            ValuePoints(runs, prices, currentHash, mainDataset, notes),
            LatencyBars(latencies, prices, mainDataset),
            prices.Currency,
            notes);
    }

    private static List<QualityRow> QualityRows(IReadOnlyList<RunRecord> runs, PriceList prices, string baselineHash, string currentHash, string mainDataset)
    {
        var rows = new List<(string Dataset, QualityRow Row)>();
        foreach (var group in runs.GroupBy(run => (run.Dataset, run.Chat.Model)))
        {
            var before = MeanGrade(group.Where(run => run.PromptsHash == baselineHash));
            var after = MeanGrade(group.Where(run => run.PromptsHash == currentHash));
            if (before is null || after is null)
            {
                continue;
            }

            var name = prices.NameOf(group.Key.Model);
            var label = group.Key.Dataset == mainDataset ? name : $"{name} · {FirstWord(group.First().Character)}";
            rows.Add((group.Key.Dataset, new QualityRow(label, before.Value, after.Value)));
        }

        return [.. rows
            .OrderBy(row => row.Dataset == mainDataset ? 0 : 1)
            .ThenByDescending(row => row.Row.After)
            .ThenBy(row => row.Row.Label, StringComparer.Ordinal)
            .Select(row => row.Row)];
    }

    private static List<ValuePoint> ValuePoints(IReadOnlyList<RunRecord> runs, PriceList prices, string currentHash, string mainDataset, List<string> notes)
    {
        var points = new List<ValuePoint>();
        foreach (var group in runs.Where(run => run.PromptsHash == currentHash && run.Dataset == mainDataset).GroupBy(run => run.Chat.Model, StringComparer.Ordinal))
        {
            if (MeanGrade(group) is not { } score)
            {
                continue;
            }

            var costs = group.Select(run => prices.CostOf(run.Chat.Model, run.Tokens.Chat)).ToList();
            double? per1000 = null;
            if (costs.All(cost => cost.HasValue))
            {
                per1000 = costs.Sum(cost => cost!.Value) / group.Sum(run => run.Results.Count) * 1000;
            }
            else
            {
                notes.Add($"{group.Key} has no price in the price file: it is left out of the chart of the price.");
            }

            points.Add(new ValuePoint(prices.NameOf(group.Key), score, per1000));
        }

        return [.. points.OrderBy(point => point.Name, StringComparer.Ordinal)];
    }

    private static List<LatencyBar> LatencyBars(IReadOnlyList<LatencyEntry> latencies, PriceList prices, string mainDataset) =>
    [
        .. latencies
            .Where(entry => entry.Dataset == mainDataset)
            .GroupBy(entry => entry.Model, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(entry => entry.RunId, StringComparer.Ordinal).First()) // the latest run of each model
            .OrderBy(entry => entry.MeanSeconds)
            .ThenBy(entry => entry.Model, StringComparer.Ordinal)
            .Select(entry => new LatencyBar(prices.NameOf(entry.Model), entry.MeanSeconds, entry.FirstTextSeconds)),
    ];

    // The mean of the grades of every graded answer of the runs (an answer the judge could not grade is left out, as in the reports).
    private static double? MeanGrade(IEnumerable<RunRecord> runs)
    {
        var grades = runs.SelectMany(run => run.Results).Where(result => result.Score.HasValue).Select(result => (double)result.Score!.Value).ToList();
        return grades.Count == 0 ? null : grades.Average();
    }

    private static string FirstWord(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
}
