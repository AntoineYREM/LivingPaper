namespace LivingPaper.Evals;

// Finds a run file from what the user typed: the path of a file, or the id of a run (its file name) in the folders where runs are kept.
internal static class RunLocator
{
    public static string Resolve(string nameOrPath, IReadOnlyList<string> folders)
    {
        if (File.Exists(nameOrPath))
        {
            return nameOrPath;
        }

        // An id is a file name, not a path: it can never lead out of the folders.
        if (nameOrPath.Contains('/', StringComparison.Ordinal) || nameOrPath.Contains('\\', StringComparison.Ordinal) || nameOrPath.Contains("..", StringComparison.Ordinal))
        {
            throw new EvalException($"No run file {nameOrPath}: give the path of an existing file, or the id of a run (no folder in it).");
        }

        var fileName = nameOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? nameOrPath : nameOrPath + ".json";
        foreach (var folder in folders)
        {
            var candidate = Path.Combine(folder, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new EvalException($"No run {nameOrPath} was found in {string.Join(" or ", folders)}. Give the path of a run file, or the id of a saved run.");
    }
}
