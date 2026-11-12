using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace LivingPaper.E2ETests;

// The base of every scenario: the application on a real port with its own empty data directory, a fake language model it can
// reach, and a Chromium page that looks at it. Nothing is shared between two tests, except the browser program.
public abstract class E2ETest : IAsyncLifetime
{
    private AppFactory? _app;
    private string? _dataDirectory;
    private IBrowserContext? _context;

    internal FakeLlm Llm { get; private set; } = null!;

    protected IPage Page { get; private set; } = null!;

    protected string AppUrl { get; private set; } = string.Empty;

    // The window of the browser. A test of the phone overrides it.
    protected virtual BrowserNewContextOptions ContextOptions() => new() { ViewportSize = new ViewportSize { Width = 1280, Height = 800 } };

    public async ValueTask InitializeAsync()
    {
        Llm = await FakeLlm.StartAsync();
        _dataDirectory = Path.Combine(Path.GetTempPath(), "livingpaper-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDirectory);
        _app = new AppFactory(_dataDirectory);
        _app.UseKestrel(0);
        _app.StartServer();
        AppUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        var browser = await SharedBrowser.GetAsync();
        var options = ContextOptions();
        options.BaseURL = AppUrl;
        options.Locale = "en-US";
        _context = await browser.NewContextAsync(options);
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = false });
        Page = await _context.NewPageAsync();
        Page.SetDefaultTimeout(30_000);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await KeepWhatHelpsIfTheTestFailedAsync();
        }
        finally
        {
            if (_context is not null)
            {
                await _context.CloseAsync();
            }

            _app?.Dispose();
            await Llm.DisposeAsync();
            if (_dataDirectory is not null && Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }

            GC.SuppressFinalize(this);
        }
    }

    // ---- What every scenario does at some point ----

    // Adds the fake language model in the settings and chooses it for the analysis and for the chat.
    protected async Task AddTheFakeModelAsync(string name = "Fake model")
    {
        await Page.GotoAsync("/Settings");
        await Page.Locator("#llm-name").FillAsync(name);
        await Page.Locator("#llm-base-url").FillAsync(Llm.BaseUrl);
        await Page.Locator("#llm-api-key").FillAsync("test-key-not-a-secret");
        await Page.Locator("#llm-model").FillAsync("fake-model");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Test and save" }).ClickAsync();
        await Assertions.Expect(Page.GetByText($"{name} was added.")).ToBeVisibleAsync();

        await Page.Locator("#llm-analysis").SelectOptionAsync(new SelectOptionValue { Label = name });
        await Page.Locator("#llm-chat").SelectOptionAsync(new SelectOptionValue { Label = name });
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save the choice" }).ClickAsync();
        await Assertions.Expect(Page.GetByText("The choice was saved.")).ToBeVisibleAsync();
    }

    // Sends the test EPUB from the page "Add a book". The page of the book opens.
    protected async Task SendTheTestBookAsync()
    {
        await Page.GotoAsync("/Add");
        await Page.Locator("#book-file").SetInputFilesAsync(new FilePayload
        {
            Name = TestEpub.FileName,
            MimeType = "application/epub+zip",
            Buffer = TestEpub.Build(),
        });
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add the book" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Book/"));
    }

    // Opens the chat of the first character of the page of a book.
    protected async Task OpenTheChatOfTheFirstCharacterAsync()
    {
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameRegex = new System.Text.RegularExpressions.Regex("^Chat( with .*)?$") }).First.ClickAsync();
        await Assertions.Expect(Page.Locator("#chat-message")).ToBeVisibleAsync();
    }

    protected async Task SayAsync(string message)
    {
        await Page.Locator("#chat-message").FillAsync(message);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
    }

    // ---- When a test fails: a screenshot and a trace (open it with `playwright show-trace`) for the artifacts of the CI ----

    private async Task KeepWhatHelpsIfTheTestFailedAsync()
    {
        if (_context is null)
        {
            return;
        }

        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        if (!failed)
        {
            await _context.Tracing.StopAsync();
            return;
        }

        var directory = Path.Combine(ArtifactsDirectory(), TestContext.Current.Test?.TestDisplayName.Replace(' ', '_').Replace('(', '_').Replace(')', '_').Replace('"', '_') ?? "test");
        Directory.CreateDirectory(directory);
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(directory, "screenshot.png"), FullPage = true });
        await _context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine(directory, "trace.zip") });
    }

    private static string ArtifactsDirectory() =>
        Environment.GetEnvironmentVariable("E2E_ARTIFACTS") is { Length: > 0 } path ? path : Path.Combine(AppContext.BaseDirectory, "e2e-artifacts");

    // The application, started for real: its data are a temporary folder, its examples are those of the repository.
    private sealed class AppFactory(string dataDirectory) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DATA_DIRECTORY", dataDirectory);
            builder.UseSetting("EXAMPLES_DIRECTORY", ExamplesDirectory());
            builder.UseEnvironment("Production");
        }

        private static string ExamplesDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "examples")))
            {
                directory = directory.Parent;
            }

            return directory is null ? throw new InvalidOperationException("The examples folder of the repository was not found.") : Path.Combine(directory.FullName, "examples");
        }
    }
}

// One Chromium for the whole run. The browser is installed by the first test if it is missing (about 170 MB, once).
internal static class SharedBrowser
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IBrowser? _browser;

    public static async Task<IBrowser> GetAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_browser is null)
            {
                // On a Linux machine with none of the libraries Chromium needs (a CI runner, a container), E2E_INSTALL_DEPS=1 also
                // installs them; CI sets it itself.
                var withDeps = Environment.GetEnvironmentVariable("E2E_INSTALL_DEPS") == "1" || Environment.GetEnvironmentVariable("CI") == "true";
                var exit = Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]);
                if (exit != 0)
                {
                    throw new InvalidOperationException($"The installation of Chromium failed (exit code {exit}).");
                }

                // The default of 5 seconds is short for an analysis that the page follows by polling.
                Assertions.SetDefaultExpectTimeout(30_000);
                var playwright = await Playwright.CreateAsync();
                _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            }

            return _browser;
        }
        finally
        {
            Gate.Release();
        }
    }
}
