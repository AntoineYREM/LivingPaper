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
        Template(PromptKeys.ChatSystem).ShouldContain("last message"); // ...that of their last message: it follows them if they change.
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

    // The chat prompt as it was shipped before the rules on the period, the services and the stage directions were added.
    private const string PreviousChatSystem = """
You play {characterName}, a character of the book "{bookTitle}". Here is your profile:
{profile}

{story}

Rules:
- You ARE {characterName}: answer in the first person, with your personality and your way of speaking.
- Answer in the language the user writes in (the language of their last message: if they change language, you change too), as in a dialogue: a few sentences, no long story.
- You know your whole story, from the beginning to the end of the book.
- Never leave your role and never say that you are an AI.
- Talk like a person texting: short messages, one to three sentences, a natural tone, always with your personality and your way of speaking.
- Never describe your gestures, movements or expressions (neither between asterisks nor between parentheses): write only what you would send in a message.
- Plain text only: no markdown, no asterisks, no lists, no titles.
""";

    // The reading prompt shipped before the passages of the books (PAPER-84).
    private const string PreviousReadPart = """
You are reading an excerpt of the book "{bookTitle}". Your notes will be used afterwards for two things: to describe the main characters of the whole book, and to tell the story of the whole book scene by scene. So note the characters, and list the scenes of the excerpt in the order they happen. Ignore everything that is not part of the story itself: a license or a copyright notice, requests for donations, information about the publisher or the edition, a table of contents, notes. They are not scenes, and the people or organizations they name are not characters.
For each character (people or creatures that act or speak, not the places or the objects), note everything the excerpt says about them. A scene is one moment of the story, in one place with the same people. Write each scene as concrete events: who does what, what is said or decided, what changes. No interpretation, no commentary, no theme, no foreshadowing, no adjectives about the mood. If something important happens (a death, a wound, a marriage, a betrayal, a departure, a revelation), say it plainly. Name in "present" only the characters who are there or directly involved and who have a name (never "a lackey" or "the crowd"). Write each one under the name the book usually calls them (for example "Cyrano", "Roxane", "De Guiche"), the same way in every scene: no full legal name, no title or alias in parentheses, no former name.
Answer only with this JSON, with no other text:
{"characters":[{"name":"most complete name","otherNames":["nicknames or other names used"],"role":"who they are and what they do in the excerpt, in one sentence","personality":"character, values, fears and desires, as the excerpt shows them","speech":"register, tone and verbal habits, without copying the text of the book","events":"what happens to them in the excerpt and their relationships with the other characters","importance":a number from 1 to 10 according to their place in the excerpt}],"scenes":[{"summary":"what happens in this scene, in one or two sentences","present":["usual name of each character present"]}]}
At most 25 characters, the most important first. At most 12 scenes, in the order of the excerpt, covering the whole excerpt up to its last lines; an excerpt with a single moment gives a single scene. Write in English, whatever the language of the book.
Important: never write straight double quotes (") inside the texts, they would break the JSON. To quote a line of dialogue, use single quotes.
""";

    [Fact]
    public async Task Ensure_GivesAnUneditedReadingPromptTheVariableOfThePassages()
    {
        await WritePromptAsync(PromptKeys.ReadPart, PreviousReadPart);

        await EnsureAsync();

        var prompts = (await ReadPromptsAsync()).GetProperty("prompts");
        prompts.GetProperty(PromptKeys.ReadPart).GetProperty("template").GetString()!.ShouldEndWith("{passageRanges}");
        prompts.GetProperty(PromptKeys.PassageRanges).GetProperty("template").GetString()!.ShouldContain("\"firstPassage\"");
        prompts.GetProperty(PromptKeys.PlotSummary).GetProperty("template").GetString()!.ShouldContain("{maxWords}");
    }

    [Fact]
    public async Task Ensure_KeepsAReadingPromptTheUserEdited_WhichThenAsksForNoPassage()
    {
        var edited = PreviousReadPart + "\nBe brief.";
        await WritePromptAsync(PromptKeys.ReadPart, edited);

        await EnsureAsync();

        (await ReadPromptsAsync()).GetProperty("prompts").GetProperty(PromptKeys.ReadPart).GetProperty("template").GetString().ShouldBe(edited);
    }

    private async Task WritePromptAsync(string key, string template, string description = "d")
    {
        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(
            PromptsFile,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                prompts = new Dictionary<string, object> { [key] = new { description, template } },
            }),
            Token);
    }

    [Fact]
    public async Task Ensure_ReplacesAPromptThatIsStillAnEarlierDefault_SoThatEveryoneGetsTheImprovement()
    {
        await WritePromptAsync(PromptKeys.ChatSystem, PreviousChatSystem, "the old description");

        await EnsureAsync();

        var entry = (await ReadPromptsAsync()).GetProperty("prompts").GetProperty(PromptKeys.ChatSystem);
        var template = entry.GetProperty("template").GetString()!;
        template.ShouldNotBe(PreviousChatSystem);
        template.ShouldContain("Last reminder: no stage directions");
        entry.GetProperty("description").GetString().ShouldNotBe("the old description");
    }

    [Fact]
    public async Task Ensure_ReplacesAnEarlierDefaultWhateverItsLineEndings()
    {
        await WritePromptAsync(PromptKeys.ChatSystem, PreviousChatSystem.ReplaceLineEndings("\r\n"));

        await EnsureAsync();

        (await ReadPromptsAsync()).GetProperty("prompts").GetProperty(PromptKeys.ChatSystem).GetProperty("template").GetString()!
            .ShouldContain("Last reminder: no stage directions");
    }

    [Fact]
    public async Task Ensure_KeepsAPromptThatTheUserEditedEvenSlightly()
    {
        var edited = PreviousChatSystem + "\n- Always call me Madame.";
        await WritePromptAsync(PromptKeys.ChatSystem, edited);

        await EnsureAsync();

        (await ReadPromptsAsync()).GetProperty("prompts").GetProperty(PromptKeys.ChatSystem).GetProperty("template").GetString().ShouldBe(edited);
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
