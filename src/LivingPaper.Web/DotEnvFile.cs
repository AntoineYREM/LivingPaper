namespace LivingPaper.Web;

// `dotnet run` does not read .env (only Docker Compose does), so in development the application reads it itself. A variable already
// set in the real environment wins over the file.
public static class DotEnvFile
{
    public static string? Find(string startFolder)
    {
        for (var folder = new DirectoryInfo(startFolder); folder is not null; folder = folder.Parent)
        {
            var path = Path.Combine(folder.FullName, ".env");
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    public static void Load(string path)
    {
        foreach (var (name, value) in Parse(path))
        {
            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    public static IEnumerable<KeyValuePair<string, string>> Parse(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            var equals = line.IndexOf('=');
            if (line.StartsWith('#') || equals < 1)
            {
                continue;
            }
            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
            {
                value = value[1..^1];
            }
            yield return KeyValuePair.Create(line[..equals].Trim(), value);
        }
    }
}
