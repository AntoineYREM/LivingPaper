using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;

namespace LivingPaper.E2ETests;

// The four journeys of PAPER-17, in a real browser, against the real application and a fake language model.

public sealed class AddALanguageModel : E2ETest
{
    [Fact]
    public async Task AModelThatIsRefused_IsNotSaved_ThenTheSameModelThatWorks_IsSaved_AndChosen()
    {
        await Page.GotoAsync("/Settings");
        string html;

        // The provider refuses the key when asked for its models: nothing is saved, and the form says why, with what was typed.
        Llm.RefuseEverything = true;
        await Page.Locator("#llm-name").FillAsync("My model");
        await Page.Locator("#llm-base-url").FillAsync(Llm.BaseUrl);
        await Page.Locator("#llm-api-key").FillAsync("wrong-key-not-a-secret");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Get the models" }).ClickAsync();
        await Assertions.Expect(Page.GetByText("The key was refused (HTTP 401)")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("No language model is saved yet")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#llm-name")).ToHaveValueAsync("My model");
        await Assertions.Expect(Page.Locator("#llm-api-key")).ToHaveValueAsync(string.Empty);

        // The provider works now: the same form, with the key typed again, lists the models; the user chooses one and saves it.
        Llm.RefuseEverything = false;
        await Page.Locator("#llm-api-key").FillAsync("right-key-not-a-secret");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Get the models" }).ClickAsync();
        await Assertions.Expect(Page.GetByText("The key works.")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#llm-model option")).ToHaveCountAsync(2);
        html = await Page.ContentAsync();
        html.ShouldNotContain("right-key-not-a-secret"); // Kept on the server between the two steps.
        await Page.Locator("#llm-model").SelectOptionAsync("fake-model");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(Page.GetByText("My model was added.")).ToBeVisibleAsync();

        // The key is never shown again, in the page or in its source.
        html = await Page.ContentAsync();
        html.ShouldNotContain("right-key-not-a-secret");

        // It is chosen for the analysis and for the chat, and the choice is kept after a reload.
        await Page.Locator("#llm-analysis").SelectOptionAsync(new SelectOptionValue { Label = "My model" });
        await Page.Locator("#llm-chat").SelectOptionAsync(new SelectOptionValue { Label = "My model" });
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save the choice" }).ClickAsync();
        await Assertions.Expect(Page.GetByText("The choice was saved.")).ToBeVisibleAsync();
        await Page.ReloadAsync();
        await Assertions.Expect(Page.Locator("#llm-analysis")).ToHaveValueAsync(new Regex(".+"));
        await Assertions.Expect(Page.Locator("#llm-chat")).ToHaveValueAsync(new Regex(".+"));
        await Assertions.Expect(Page.GetByText("Analysis is off")).ToHaveCountAsync(0);
    }
}

public sealed class AnalyzeABook : E2ETest
{
    [Fact]
    public async Task AnEpubIsAnalyzed_TheLibraryShowsItRunning_ThenTheCharactersAppear()
    {
        await AddTheFakeModelAsync();
        Llm.HoldTheAnalysis();

        await SendTheTestBookAsync();

        // While the model has not answered, the library says the analysis is running, with its progress.
        await Page.GotoAsync("/");
        var row = Page.Locator(".contents__item", new PageLocatorOptions { HasText = TestEpub.Title });
        await Assertions.Expect(row).ToBeVisibleAsync();
        await Assertions.Expect(row.Locator(".status")).ToHaveTextAsync("Analysis in progress");
        await Assertions.Expect(row.Locator("progress")).ToBeVisibleAsync();

        // The model answers: the list changes by itself, with no reload.
        Llm.Release();
        await Assertions.Expect(row.Locator(".status")).ToHaveTextAsync("Analyzed");

        // The page of the book shows the summary and the characters, with the spoiler warning.
        await row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = TestEpub.Title }).First.ClickAsync();
        await Assertions.Expect(Page.GetByText("A lonely island at the end of the nineteenth century")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Characters know the whole book")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Marguerite Voss", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Captain Alder", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
    }
}

public sealed class ChatWithACharacter : E2ETest
{
    [Fact]
    public async Task TheReplyArrivesPieceByPiece_ThenTheFullReply_AndTheModelPlaysTheCharacter()
    {
        await AddTheFakeModelAsync();
        await SendTheTestBookAsync();
        await Assertions.Expect(Page.GetByText("Marguerite Voss", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await OpenTheChatOfTheFirstCharacterAsync();

        // The model sends the first piece and waits: the page shows a reply that is not finished.
        Llm.HoldTheChatAfterTheFirstPiece();
        await SayAsync("Hello, who are you?");
        var log = Page.Locator("#chat-log");
        await Assertions.Expect(log).ToContainTextAsync("Hello, who are you?");
        await Assertions.Expect(log).ToContainTextAsync(FakeLlm.FirstPiece.Trim());
        await Assertions.Expect(log).Not.ToContainTextAsync("Sit by the fire");

        // The model finishes: the whole reply is there.
        Llm.Release();
        await Assertions.Expect(log).ToContainTextAsync(FakeLlm.Reply);

        // The model was given the profile of the character and the conversation, not only the message.
        var body = Llm.Bodies.Last(b => b.Contains("You play ", StringComparison.Ordinal));
        body.ShouldContain("Marguerite Voss");
        body.ShouldContain("Hello, who are you?");
    }

    [Fact]
    public async Task WhenTheModelRefuses_TheErrorIsShownInTheWindow_AndTheMessageGoesBackIntoTheField()
    {
        await AddTheFakeModelAsync();
        await SendTheTestBookAsync();
        await Assertions.Expect(Page.GetByText("Marguerite Voss", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await OpenTheChatOfTheFirstCharacterAsync();

        Llm.RefuseTheChat = true;
        await SayAsync("Anyone there?");

        await Assertions.Expect(Page.Locator("#chat-error")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#chat-error-text")).ToContainTextAsync("The provider refused the request");
        await Assertions.Expect(Page.Locator("#chat-message")).ToHaveValueAsync("Anyone there?");
    }
}

public sealed class TheExamples : E2ETest
{
    [Fact]
    public async Task AnExampleOpensWithoutAnalysis_AndYouCanChatWithItsCharacters()
    {
        await AddTheFakeModelAsync();

        await Page.GotoAsync("/Examples");
        await Assertions.Expect(Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Le Malade imaginaire", Exact = true })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Le Malade imaginaire" }).First.ClickAsync();

        // It is read only: it is marked as an example, and says so.
        await Assertions.Expect(Page.GetByText("This book is an example, and is read only")).ToBeVisibleAsync();
        await OpenTheChatOfTheFirstCharacterAsync();

        await SayAsync("Bonjour !");
        await Assertions.Expect(Page.Locator("#chat-log")).ToContainTextAsync(FakeLlm.Reply);
        Llm.Bodies.Last(b => b.Contains("You play ", StringComparison.Ordinal)).ShouldContain("Le Malade imaginaire");
    }
}
