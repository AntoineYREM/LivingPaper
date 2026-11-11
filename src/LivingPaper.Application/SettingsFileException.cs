namespace LivingPaper.Application;

// The settings file cannot be used. The message names the file and says what is wrong; it never contains a key, an
// address or any other value from the file, so it can be logged and shown.
public sealed class SettingsFileException(string fileName, string problem) : Exception($"{fileName}: {problem}")
{
    public string FileName { get; } = fileName;

    public string Problem { get; } = problem;
}
