namespace LivingPaper.Application;

// Whether two names are those of one embedding model: its vectors can then be compared.
internal static class EmbeddingModelNames
{
    // A local server gives "bge-m3" and "bge-m3:latest" for the same model.
    public static bool Same(string first, string second) => string.Equals(Untagged(first), Untagged(second), StringComparison.OrdinalIgnoreCase);

    private static string Untagged(string model)
    {
        const string latest = ":latest";
        var name = model.Trim();
        return name.EndsWith(latest, StringComparison.OrdinalIgnoreCase) ? name[..^latest.Length] : name;
    }
}
