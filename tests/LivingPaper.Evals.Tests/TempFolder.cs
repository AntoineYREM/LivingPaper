namespace LivingPaper.Evals.Tests;

// A folder in the temp directory, deleted with the test.
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "livingpaper-evals-" + Guid.NewGuid().ToString("N"));

    public string Write(string name, string content)
    {
        var path = System.IO.Path.Combine(Path, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
