using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Web.IntegrationTests;

// The real application, started in memory with a temporary data directory so that tests never touch ./data.
// Files can be put in DataDirectory before the first request (the application starts on first use).
// dataDirectory: pass the directory of a first factory to start the application again on the same data, as a restart
// would; the first factory then owns the cleaning. configureServices: replaces services, for example the language model
// client, which tests never let reach a real provider.
public sealed class LivingPaperFactory : WebApplicationFactory<Program>
{
    private readonly bool _ownsDirectory;
    private readonly Action<IServiceCollection>? _configureServices;

    // xUnit builds a fixture with the only public constructor, so this one takes no argument; With(...) is for the rest.
    public LivingPaperFactory()
        : this(null, null)
    {
    }

    private LivingPaperFactory(string? dataDirectory, Action<IServiceCollection>? configureServices)
    {
        _ownsDirectory = dataDirectory is null;
        DataDirectory = dataDirectory ?? Path.Combine(Path.GetTempPath(), "livingpaper-web-tests-" + Guid.NewGuid().ToString("N"));
        _configureServices = configureServices;
        Directory.CreateDirectory(DataDirectory);
    }

    public static LivingPaperFactory With(string? dataDirectory = null, Action<IServiceCollection>? configureServices = null) =>
        new(dataDirectory, configureServices);

    public string DataDirectory { get; }

    public ListLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("DATA_DIRECTORY", DataDirectory);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services => _configureServices?.Invoke(services));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsDirectory && Directory.Exists(DataDirectory))
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
