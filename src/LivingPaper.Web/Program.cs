// Composition root: the only place where the layers are wired together.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var app = builder.Build();

app.MapRazorPages();

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the integration tests.
public partial class Program;
