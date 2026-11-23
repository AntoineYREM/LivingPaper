namespace LivingPaper.Evals;

// The `compare` command: reads two run files (nothing is sent anywhere, Langfuse is not needed) and prints how they differ.
internal static class CompareCommand
{
    public static async Task<int> RunAsync(CompareOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        var evalsFolder = RepoFiles.Find("evals") ?? Path.Combine(Directory.GetCurrentDirectory(), "evals");
        string[] folders = [Path.Combine(evalsFolder, "runs"), Path.Combine(evalsFolder, "published")];

        var a = await RunStore.LoadAsync(RunLocator.Resolve(options.RunA, folders), cancellationToken).ConfigureAwait(false);
        var b = await RunStore.LoadAsync(RunLocator.Resolve(options.RunB, folders), cancellationToken).ConfigureAwait(false);

        // The prices are optional: without the file the comparison is shown with the tokens only, and says so.
        var pricesPath = options.PricesPath ?? RepoFiles.Find("evals/prices.json");
        var prices = pricesPath is null ? null : await PriceList.ReadAsync(pricesPath, cancellationToken).ConfigureAwait(false);

        ComparisonWriter.Write(output, RunComparison.Compare(a, b), prices);
        return 0;
    }
}
