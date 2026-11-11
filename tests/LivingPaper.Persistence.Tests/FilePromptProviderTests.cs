using LivingPaper.Application;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

public sealed class FilePromptProviderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FilePromptProvider _provider;

    public FilePromptProviderTests()
    {
        Directory.CreateDirectory(_temp.Path);
        _provider = new FilePromptProvider(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string PromptsFile => Path.Combine(_temp.Path, FilePromptProvider.FileName);

    private static Dictionary<string, string> Values(params (string Name, string Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value);

    // A minimal valid file: every key present, each prompt a one-line text, replaced by the entries given.
    private static string FileWith(params (string Key, string Template)[] overrides)
    {
        var templates = PromptKeys.All.ToDictionary(key => key, key => "Text of " + key);
        foreach (var (key, template) in overrides)
        {
            templates[key] = template;
        }

        var entries = templates.Select(pair => $"\"{pair.Key}\": {{ \"description\": \"d\", \"template\": {System.Text.Json.JsonSerializer.Serialize(pair.Value)} }}");
        return "{ \"schemaVersion\": 1, \"prompts\": { " + string.Join(", ", entries) + " } }";
    }

    private async Task WriteFileAsync(string content) => await File.WriteAllTextAsync(PromptsFile, content, Token);

    [Fact]
    public async Task Render_ReplacesTheVariablesOfThePromptInTheFile()
    {
        await WriteFileAsync(FileWith((PromptKeys.ChatSystem, "You are {characterName} in {bookTitle}. Profile: {profile}")));

        var text = await _provider.RenderAsync(
            PromptKeys.ChatSystem,
            Values((PromptVariables.CharacterName, "Argan"), (PromptVariables.BookTitle, "Le Malade imaginaire"), (PromptVariables.Profile, "A hypochondriac.")),
            Token);

        text.ShouldBe("You are Argan in Le Malade imaginaire. Profile: A hypochondriac.");
    }

    [Fact]
    public async Task Render_ReadsTheFileAgainAtEveryCall()
    {
        await WriteFileAsync(FileWith((PromptKeys.ConnectionTest, "first version")));
        (await _provider.RenderAsync(PromptKeys.ConnectionTest, Values(), Token)).ShouldBe("first version");

        await WriteFileAsync(FileWith((PromptKeys.ConnectionTest, "edited by hand")));

        (await _provider.RenderAsync(PromptKeys.ConnectionTest, Values(), Token)).ShouldBe("edited by hand");
    }

    [Fact]
    public async Task Render_DoesNotExpandAPlaceholderQuotedInsideAValue()
    {
        await WriteFileAsync(FileWith((PromptKeys.ChatSystem, "{characterName}: {profile}")));

        var text = await _provider.RenderAsync(
            PromptKeys.ChatSystem,
            Values((PromptVariables.CharacterName, "Argan"), (PromptVariables.BookTitle, "x"), (PromptVariables.Profile, "I repeat {characterName}")),
            Token);

        text.ShouldBe("Argan: I repeat {characterName}");
    }

    [Fact]
    public async Task Render_AcceptsAPromptThatDoesNotUseEveryAllowedVariable()
    {
        await WriteFileAsync(FileWith((PromptKeys.MergeCharacters, "Keep the best {maxCharacters}.")));

        var text = await _provider.RenderAsync(
            PromptKeys.MergeCharacters,
            Values((PromptVariables.BookTitle, "x"), (PromptVariables.MaxCharacters, "10")),
            Token);

        text.ShouldBe("Keep the best 10.");
    }

    [Fact]
    public async Task Render_ThrowsAClearError_WhenTheFileDoesNotExist()
    {
        var exception = await Should.ThrowAsync<PromptFileException>(() => _provider.RenderAsync(PromptKeys.ConnectionTest, Values(), Token));

        exception.FileName.ShouldBe(FilePromptProvider.FileName);
        exception.Problem.ShouldContain("not found");
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{ \"schemaVersion\": 1 }")]
    [InlineData("{ \"schemaVersion\": 1, \"prompts\": [] }")]
    public async Task Render_ThrowsAClearError_WhenTheFileIsNotValid(string content)
    {
        await WriteFileAsync(content);

        var exception = await Should.ThrowAsync<PromptFileException>(() => _provider.RenderAsync(PromptKeys.ConnectionTest, Values(), Token));

        exception.FileName.ShouldBe(FilePromptProvider.FileName);
    }

    [Fact]
    public async Task Render_ThrowsAClearError_WhenTheSchemaVersionIsNotSupported()
    {
        await WriteFileAsync(FileWith().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal));

        var exception = await Should.ThrowAsync<PromptFileException>(() => _provider.RenderAsync(PromptKeys.ConnectionTest, Values(), Token));

        exception.Problem.ShouldContain("schemaVersion");
    }

    [Fact]
    public async Task Render_ThrowsAnErrorThatNamesTheKey_WhenThePromptIsMissing()
    {
        await WriteFileAsync(FileWith().Replace("\"chat.system\"", "\"chat.other\"", StringComparison.Ordinal));

        var exception = await Should.ThrowAsync<PromptFileException>(() => _provider.RenderAsync(PromptKeys.ChatSystem, Values(), Token));

        exception.Key.ShouldBe(PromptKeys.ChatSystem);
        exception.Message.ShouldContain(FilePromptProvider.FileName);
        exception.Message.ShouldContain(PromptKeys.ChatSystem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Render_ThrowsAnErrorThatNamesTheKey_WhenThePromptIsEmpty(string template)
    {
        await WriteFileAsync(FileWith((PromptKeys.BookSummary, template)));

        var exception = await Should.ThrowAsync<PromptFileException>(
            () => _provider.RenderAsync(PromptKeys.BookSummary, Values((PromptVariables.BookTitle, "x")), Token));

        exception.Key.ShouldBe(PromptKeys.BookSummary);
        exception.Problem.ShouldContain("empty");
    }

    [Fact]
    public async Task Render_ThrowsAnErrorThatNamesTheKeyAndTheVariable_WhenTheVariableIsUnknown()
    {
        await WriteFileAsync(FileWith((PromptKeys.ReadPart, "Read {bookTitle}, written by {author}.")));

        var exception = await Should.ThrowAsync<PromptFileException>(
            () => _provider.RenderAsync(PromptKeys.ReadPart, Values((PromptVariables.BookTitle, "x")), Token));

        exception.Key.ShouldBe(PromptKeys.ReadPart);
        exception.Problem.ShouldContain("{author}");
        exception.Problem.ShouldContain("{bookTitle}"); // The allowed variables help the person who edits the file.
    }

    [Fact]
    public async Task Render_RefusesAVariableThatBelongsToAnotherPrompt()
    {
        await WriteFileAsync(FileWith((PromptKeys.ConnectionTest, "Say {profile}")));

        await Should.ThrowAsync<PromptFileException>(() => _provider.RenderAsync(PromptKeys.ConnectionTest, Values(), Token));
    }

    [Fact]
    public async Task Render_NeverPutsAValueOrThePromptTextInTheError()
    {
        await WriteFileAsync(FileWith((PromptKeys.ReadPart, "TOP SECRET TEMPLATE TEXT {bookTitle} {author}")));

        var exception = await Should.ThrowAsync<PromptFileException>(
            () => _provider.RenderAsync(PromptKeys.ReadPart, Values((PromptVariables.BookTitle, "A PASSAGE OF THE BOOK")), Token));

        exception.Message.ShouldNotContain("A PASSAGE OF THE BOOK");
        exception.Message.ShouldNotContain("TOP SECRET TEMPLATE TEXT");
    }

    [Fact]
    public async Task Render_ANotherPromptBeingBroken_DoesNotPreventThisOneFromWorking()
    {
        await WriteFileAsync(FileWith((PromptKeys.ReadPart, "{nope}"), (PromptKeys.ConnectionTest, "pong please")));

        (await _provider.RenderAsync(PromptKeys.ConnectionTest, Values(), Token)).ShouldBe("pong please");
    }

    [Fact]
    public async Task Render_ThrowsArgumentException_WhenTheCodeAsksForAnUnknownKey()
    {
        await WriteFileAsync(FileWith());

        await Should.ThrowAsync<ArgumentException>(() => _provider.RenderAsync("analysis.nothing", Values(), Token));
    }

    [Fact]
    public async Task Render_ThrowsArgumentException_WhenTheCodeDoesNotGiveAValueThePromptNeeds()
    {
        await WriteFileAsync(FileWith((PromptKeys.ChatSystem, "{characterName} {profile}")));

        await Should.ThrowAsync<ArgumentException>(
            () => _provider.RenderAsync(PromptKeys.ChatSystem, Values((PromptVariables.CharacterName, "Argan")), Token));
    }

    [Fact]
    public async Task Render_IgnoresFieldsOfTheFileThatItDoesNotKnow()
    {
        await WriteFileAsync(FileWith((PromptKeys.ConnectionTest, "ok")).Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"note\": \"mine\",", StringComparison.Ordinal));

        (await _provider.RenderAsync(PromptKeys.ConnectionTest, Values(), Token)).ShouldBe("ok");
    }

    [Fact]
    public async Task Render_DoesNotRequireADescription()
    {
        await WriteFileAsync("{ \"schemaVersion\": 1, \"prompts\": { \"llm.connectionTest\": { \"template\": \"ok\" } } }");

        (await _provider.RenderAsync(PromptKeys.ConnectionTest, Values(), Token)).ShouldBe("ok");
    }
}
