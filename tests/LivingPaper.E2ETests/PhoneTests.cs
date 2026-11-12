using Microsoft.Playwright;
using Shouldly;

namespace LivingPaper.E2ETests;

// The same journeys on a phone: a screen of 390 x 844 points, touch, a pixel ratio of 2. The page must never scroll sideways,
// and what the user needs (the field, the Send button) must be inside the screen.
public sealed class OnAPhone : E2ETest
{
    protected override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        DeviceScaleFactor = 2,
        IsMobile = true,
        HasTouch = true,
    };

    [Fact]
    public async Task EveryPageOfTheJourney_FitsTheScreen_AndTheChatWorks()
    {
        await AddTheFakeModelAsync();
        await ShouldFitAsync("settings");

        await Page.GotoAsync("/");
        await ShouldFitAsync("library");

        await Page.GotoAsync("/Examples");
        await ShouldFitAsync("examples");

        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Le Malade imaginaire" }).First.ClickAsync();
        await Assertions.Expect(Page.GetByText("This book is an example, and is read only")).ToBeVisibleAsync();
        await ShouldFitAsync("book");

        await OpenTheChatOfTheFirstCharacterAsync();
        await ShouldFitAsync("chat");

        // The composer is inside the screen, and a message can be sent by touch.
        var send = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" });
        var box = await send.BoundingBoxAsync();
        box.ShouldNotBeNull();
        (box.X >= 0 && box.X + box.Width <= 390).ShouldBeTrue("the Send button is outside the screen");
        await Page.Locator("#chat-message").FillAsync("Bonjour !");
        await send.TapAsync();
        await Assertions.Expect(Page.Locator("#chat-log")).ToContainTextAsync(FakeLlm.Reply);
        await ShouldFitAsync("chat with a conversation");
    }

    private async Task ShouldFitAsync(string what)
    {
        var overflow = await Page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
        overflow.ShouldBeLessThanOrEqualTo(0, $"the page \"{what}\" scrolls sideways by {overflow} px on a phone");
    }
}
