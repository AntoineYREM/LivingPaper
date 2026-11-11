namespace LivingPaper.Web.IntegrationTests;

// Finds a file of the repository from where the tests run, for tests that read the source files themselves.
internal static class RepositoryFiles
{
    public static string Find(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{relativePath} was not found above the test directory.");
    }
}
