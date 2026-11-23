namespace LivingPaper.Evals;

// Finds a file or a folder of the repository (evals/judge.json, examples) by going up from the current folder, then from the folder of
// the tool, so the command works from the root of the repository or from anywhere below it.
internal static class RepoFiles
{
    // fromToolFolderToo: false looks only upward from startFolder (used where the answer must not depend on where the tool is installed).
    public static string? Find(string relativePath, string? startFolder = null, bool fromToolFolderToo = true)
    {
        string[] starts = fromToolFolderToo
            ? [startFolder ?? Directory.GetCurrentDirectory(), AppContext.BaseDirectory]
            : [startFolder ?? Directory.GetCurrentDirectory()];
        foreach (var start in starts)
        {
            for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
            {
                var candidate = Path.Combine(folder.FullName, relativePath);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
