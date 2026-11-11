using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

public sealed class PromptFileSynchronizerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DataDirectory => Path.Combine(_temp.Path, "data");

    private string PromptsFile => Path.Combine(DataDirectory, FilePromptProvider.FileName);

    private Task EnsureAsync() => PromptFileSynchronizer.EnsureAsync(DataDirectory, Token);

    private async Task<JsonElement> ReadPromptsAsync()
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(PromptsFile, Token));
        return json.RootElement.Clone();
    }

    [Fact]
    public async Task Ensure_CreatesTheDataDirectoryAndTheFileWithEveryDefaultPrompt_OnTheFirstStart()
    {
        await EnsureAsync();

        var root = await ReadPromptsAsync();
        root.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        root.GetProperty("prompts").EnumerateObject().Select(property => property.Name).Order().ShouldBe(PromptKeys.All.Order());
    }

    [Fact]
    public async Task Ensure_WritesAReadableFile()
    {
        await EnsureAsync();

        var text = await File.ReadAllTextAsync(PromptsFile, Token);
        text.ShouldContain("\n"); // Indented, one property per line.
        text.ShouldNotContain("\\u0027"); // Apostrophes and brackets stay readable, not escaped.
        text.ShouldNotContain("\\u003C");
    }

    [Fact]
    public async Task Ensure_DefaultPrompts_AreEachValid_AndDescribeTheirVariables()
    {
        await EnsureAsync();

        var prompts = (await ReadPromptsAsync()).GetProperty("prompts");
        foreach (var key in PromptKeys.All)
        {
            var entry = prompts.GetProperty(key);
            var description = entry.GetProperty("description").GetString()!;
            var template = PromptTemplate.Parse(entry.GetProperty("template").GetString()!);

            description.ShouldNotBeNullOrWhiteSpace();
            template.FindUnknownVariables(PromptKeys.VariablesOf(key)).ShouldBeEmpty($"{key} uses an unknown variable");
            foreach (var variable in PromptKeys.VariablesOf(key))
            {
                description.ShouldContain("{" + variable + "}", customMessage: $"the description of {key} must list {{{variable}}}");
            }
        }
    }

    [Fact]
    public async Task Ensure_DefaultPrompts_AreAllUsableThroughTheProvider()
    {
        await EnsureAsync();
        var provider = new FilePromptProvider(DataDirectory);

        foreach (var key in PromptKeys.All)
        {
            var values = PromptKeys.VariablesOf(key).ToDictionary(name => name, name => "<" + name + ">");
            var text = await provider.RenderAsync(key, values, Token);
            text.ShouldNotBeNullOrWhiteSpace();
            text.ShouldNotMatch(@"\{[A-Za-z][A-Za-z0-9]*\}", $"{key} still has an unreplaced variable");
        }
    }

    [Fact]
    public async Task Ensure_DefaultPrompts_FollowTheRulesOfTheProject()
    {
        await EnsureAsync();
        var prompts = (await ReadPromptsAsync()).GetProperty("prompts");
        string Template(string key) => prompts.GetProperty(key).GetProperty("template").GetString()!;

        Template(PromptKeys.MergeCharacters).ShouldContain("{maxCharacters}"); // The number of characters is a variable.
        Template(PromptKeys.ChatSystem).ShouldContain("language the user writes in"); // Chat answers in the user's language.
        Template(PromptKeys.ChatSystem).ShouldContain("{profile}");
        Template(PromptKeys.ReadPart).ShouldContain("English"); // Data written by the analysis is in English.
    }

    [Fact]
    public async Task Ensure_LeavesAnUpToDateFileUntouched()
    {
        await EnsureAsync();
        var before = await File.ReadAllBytesAsync(PromptsFile, Token);

        await EnsureAsync();

        (await File.ReadAllBytesAsync(PromptsFile, Token)).ShouldBe(before);
    }

    [Fact]
    public async Task Ensure_AddsAMissingPrompt_WithoutTouchingTheOthers()
    {
        Directory.CreateDirectory(DataDirectory);
        var edited = "My own <chat> prompt, don't change it: {characterName}";
        await File.WriteAllTextAsync(
            PromptsFile,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                prompts = new Dictionary<string, object> { [PromptKeys.ChatSystem] = new { description = "mine", template = edited } },
            }),
            Token);

        await EnsureAsync();

        var prompts = (await ReadPromptsAsync()).GetProperty("prompts");
        prompts.EnumerateObject().Select(property => property.Name).Order().ShouldBe(PromptKeys.All.Order());
        prompts.GetProperty(PromptKeys.ChatSystem).GetProperty("template").GetString().ShouldBe(edited);
        prompts.GetProperty(PromptKeys.ChatSystem).GetProperty("description").GetString().ShouldBe("mine");
        prompts.GetProperty(PromptKeys.ReadPart).GetProperty("template").GetString().ShouldNotBeNullOrWhiteSpace();
        (await File.ReadAllTextAsync(PromptsFile, Token)).ShouldContain("don't change it"); // Not escaped when rewritten.
    }

    [Fact]
    public async Task Ensure_NeverOverwritesAnEditedPrompt_EvenWhenItIsEmptyOrInvalid()
    {
        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(
            PromptsFile,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                prompts = new Dictionary<string, object>
                {
                    [PromptKeys.ReadPart] = new { description = "d", template = "" },
                    [PromptKeys.ChatSystem] = new { description = "d", template = "{unknownVariable}" },
                },
            }),
            Token);

        await EnsureAsync();

        var prompts = (await ReadPromptsAsync()).GetProperty("prompts");
        prompts.GetProperty(PromptKeys.ReadPart).GetProperty("template").GetString().ShouldBeEmpty();
        prompts.GetProperty(PromptKeys.ChatSystem).GetProperty("template").GetString().ShouldBe("{unknownVariable}");
    }

    [Fact]
    public async Task Ensure_KeepsAKeyThatItDoesNotKnow()
    {
        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(
            PromptsFile,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                prompts = new Dictionary<string, object> { ["my.custom"] = new { description = "mine", template = "kept" } },
            }),
            Token);

        await EnsureAsync();

        (await ReadPromptsAsync()).GetProperty("prompts").GetProperty("my.custom").GetProperty("template").GetString().ShouldBe("kept");
    }

    [Fact]
    public async Task Ensure_LeavesNoTemporaryFileBehind()
    {
        await EnsureAsync();

        Directory.GetFiles(DataDirectory).ShouldBe([PromptsFile]);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{ \"schemaVersion\": 99, \"prompts\": {} }")]
    [InlineData("{ \"schemaVersion\": 1 }")]
    public async Task Ensure_ThrowsAClearError_AndLeavesAFileThatIsNotValidUntouched(string content)
    {
        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(PromptsFile, content, Token);

        var exception = await Should.ThrowAsync<PromptFileException>(EnsureAsync);

        exception.FileName.ShouldBe(FilePromptProvider.FileName);
        (await File.ReadAllTextAsync(PromptsFile, Token)).ShouldBe(content);
        Directory.GetFiles(DataDirectory).ShouldBe([PromptsFile]);
    }
}
