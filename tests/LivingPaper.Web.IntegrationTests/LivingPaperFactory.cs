using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Web.IntegrationTests;

// The real application, started in memory with a temporary data directory so that tests never touch ./data.
// Files can be put in DataDirectory before the first request (the application starts on first use).
public sealed class LivingPaperFactory : WebApplicationFactory<Program>
{
    public LivingPaperFactory() => Directory.CreateDirectory(DataDirectory);

    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "livingpaper-web-tests-" + Guid.NewGuid().ToString("N"));

    public ListLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("DATA_DIRECTORY", DataDirectory);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}

// Keeps every log line in memory so a test can check what the application reported.
public sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new ListLogger(_entries);

    public void Dispose()
    {
    }

    private sealed class ListLogger(List<(LogLevel Level, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            lock (entries)
            {
                entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
