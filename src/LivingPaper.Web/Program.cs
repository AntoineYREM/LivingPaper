using LivingPaper.Application;
using LivingPaper.Persistence;
using LivingPaper.Web;

// Composition root: the only place where the layers are wired together.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Everything the application stores (prompts, books, settings) lives in this one directory: a Docker volume in
// production. Set DATA_DIRECTORY to move it; by default it is ./data next to where the application runs.
var dataDirectory = Path.GetFullPath(builder.Configuration["DATA_DIRECTORY"] ?? "data");
builder.Services.AddSingleton<IPromptProvider>(new FilePromptProvider(dataDirectory));

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

app.MapRazorPages();

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the integration tests.
public partial class Program;
