namespace LivingPaper.Persistence;

// The earlier versions of a default prompt, kept as hashes. A prompt in data/prompts.json that is still exactly one of them was never edited by the
// user, so an update can replace it by the new default without losing anything: everybody gets the improvement. An edited prompt is never touched.
// Add the hash of the previous default here (SHA-256 of the template, line endings as LF, in lowercase hex) each time a default prompt is changed.
internal static class ReplacedDefaults
{
    private static readonly Dictionary<string, HashSet<string>> Hashes = new(StringComparer.Ordinal)
    {
        ["chat.system"] =
        [
            "ad28d64e01a6818fcfddec26d7c12d60478dc9747903ee337383627b9c44b3f8",
            "dc2d351574b4c255bf44fef9233392bf51a426cd3d9a82c95ab0d24e354b0bb3",
            "534ce5991fb332d7d02d7a0d76576cae8180265690a9117d7098057dcb3df7ad",
        ],
    };

    public static bool WasShipped(string key, string template)
    {
        if (!Hashes.TryGetValue(key, out var known))
        {
            return false;
        }

        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(template.ReplaceLineEndings("\n"))));
        return known.Contains(hash);
    }
}
