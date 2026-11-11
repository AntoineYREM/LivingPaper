using LivingPaper.Application;
using LivingPaper.Infrastructure;
using LivingPaper.Persistence;
using LivingPaper.Web;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.DataProtection;

// Composition root: the only place where the layers are wired together.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// The texts of the interface, in English and French (Resources/*.json, embedded). An invalid file stops the application here.
builder.Services.AddTextLocalization();

// Everything the application stores (prompts, books, settings) lives in this one directory: a Docker volume in
// production. Set DATA_DIRECTORY to move it; by default it is ./data next to where the application runs.
var dataDirectory = Path.GetFullPath(builder.Configuration["DATA_DIRECTORY"] ?? "data");
builder.Services.AddSingleton<IPromptProvider>(new FilePromptProvider(dataDirectory));

// One factory for the whole application: its clients share a connection pool. It builds a client for each language
// model of the settings (PAPER-5) when the analysis or the chat needs one.
builder.Services.AddSingleton<IChatModelFactory, OpenAiCompatibleChatModelFactory>();

// The API keys of the language models are encrypted before they are written to data/settings.json. The encryption keys
// are kept in data/keys, so a restart can read them again; deleting that folder makes the saved keys unreadable (the
// settings then say so and the models must be added again). In a container (Linux) they are not themselves encrypted:
// see the README for what that protects against.
builder.Services.AddDataProtection()
    .SetApplicationName("LivingPaper")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddSingleton<ILlmSettingsRepository>(services =>
    new JsonLlmSettingsRepository(dataDirectory, services.GetRequiredService<ISecretProtector>()));
builder.Services.AddSingleton<ILlmConnectionTester, LlmConnectionTester>();
builder.Services.AddSingleton<LlmSettingsService>();

// The books (PAPER-7): one JSON file each in data/books, and the text of a book kept in data/uploads only until its
// analysis has finished. The reader of EPUB and PDF files is an adapter; the service is what the pages call.
builder.Services.AddSingleton<IBookRepository>(new JsonBookRepository(Path.Combine(dataDirectory, "books")));
builder.Services.AddSingleton<IBookTextStore>(new FileBookTextStore(Path.Combine(dataDirectory, "uploads")));
builder.Services.AddSingleton<IBookTextExtractor, BookTextExtractor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BookService>();

// The analysis of the books (PAPER-8): a background service runs it one book at a time, with the language model chosen in
// the settings. The pages talk to it through IAnalysisScheduler. ANALYSIS_PAGES_PER_PART and ANALYSIS_PARALLEL_CALLS tune it.
builder.Services.AddSingleton(AnalysisSettings.Read(builder.Configuration));
builder.Services.AddSingleton<IAnalysisStepStore>(new JsonAnalysisStepStore(Path.Combine(dataDirectory, "books")));
builder.Services.AddSingleton<BookAnalyzer>();
builder.Services.AddSingleton<BookAnalysisWorker>();
builder.Services.AddSingleton<IAnalysisScheduler>(services => services.GetRequiredService<BookAnalysisWorker>());
builder.Services.AddHostedService(services => services.GetRequiredService<BookAnalysisWorker>());

var app = builder.Build();

try
{
    await PromptFileSynchronizer.EnsureAsync(dataDirectory, app.Lifetime.ApplicationStopping);
}
catch (PromptFileException exception)
{
    // The application starts anyway so the user can still reach the pages; the same error is raised, with the same
    // message, each time a prompt is used. The file is never overwritten. The message holds no prompt text.
    app.Logger.PromptsFileUnusable(exception.Message);
}

// Serves wwwroot: the style sheets, the fonts and the images of the application, all its own (nothing from a CDN).
app.UseStaticFiles();
app.UseTextLocalization();
app.MapRazorPages();

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the integration tests.
public partial class Program;
