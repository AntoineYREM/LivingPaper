using System.Net;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The icon of the site: an open book, black on white. The tab of the browser shows it, and so does a bookmark or a phone's home screen.
public class FaviconTests(LivingPaperFactory factory) : IClassFixture<LivingPaperFactory>
{
    [Theory]
    [InlineData("/", "/favicon.svg")]
    [InlineData("/", "/favicon.ico")]
    [InlineData("/", "/apple-touch-icon.png")]
    [InlineData("/settings", "/favicon.svg")]
    [InlineData("/examples", "/favicon.svg")]
    public async Task EveryPage_DeclaresTheIcon(string page, string icon)
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(page, TestContext.Current.CancellationToken);

        html.ShouldContain($"href=\"{icon}");
    }

    [Theory]
    [InlineData("/favicon.svg", "image/svg+xml")]
    [InlineData("/favicon.ico", "image/x-icon")]
    [InlineData("/apple-touch-icon.png", "image/png")]
    public async Task TheIcons_AreServed(string path, string contentType)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBeOneOf(contentType, "image/vnd.microsoft.icon");
    }
}
