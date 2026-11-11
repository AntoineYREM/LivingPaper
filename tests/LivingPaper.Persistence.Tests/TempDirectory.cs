namespace LivingPaper.Persistence.Tests;

// A directory that exists only for the duration of a test.
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "livingpaper-tests-" + Guid.NewGuid().ToString("N"));

    public string Path { get; }

    public string BooksPath => System.IO.Path.Combine(Path, "books");

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
