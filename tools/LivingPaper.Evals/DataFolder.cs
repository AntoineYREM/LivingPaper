namespace LivingPaper.Evals;

// Which data folder to read the language models from. The site keeps its data in DATA_DIRECTORY or, when that is not set, in ./data next
// to where it runs: for `dotnet run` of the site that is src/LivingPaper.Web/data, not the root of the repository. So that the tool shows
// the same list as the Settings page without being told, it looks for the folder that really holds settings.json.
internal static class DataFolder
{
    private const string SettingsFile = "settings.json";

    // 1. DATA_DIRECTORY when set. 2. ./data of the current folder when it holds settings.json. 3. the data folder of the site in
    // the repository (src/LivingPaper.Web/data), found by going up from the current folder. 4. ./data, as the site would.
    public static string Resolve(string? configured, string currentFolder)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var local = Path.Combine(currentFolder, "data");
        if (File.Exists(Path.Combine(local, SettingsFile)))
        {
            return local;
        }

        var site = RepoFiles.Find($"src/LivingPaper.Web/data/{SettingsFile}", currentFolder, fromToolFolderToo: false);
        return site is null ? local : Path.GetDirectoryName(site)!;
    }
}
