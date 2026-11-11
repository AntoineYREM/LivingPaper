using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// Smoke test: the real application starts in memory and serves its home page.
public class HomePageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Home_ReturnsOk_AndShowsTheProjectName()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("LivingPaper");
    }

    [Fact]
    public async Task UnknownPage_ReturnsNotFound()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/this-page-does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
