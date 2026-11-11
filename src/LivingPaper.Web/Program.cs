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
