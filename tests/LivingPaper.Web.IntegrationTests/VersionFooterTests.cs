using System.Net;
using System.Text.RegularExpressions;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The version number is defined once, in Directory.Build.props, and shown at the foot of every page (PAPER-20).
public partial class VersionFooterTests(LivingPaperFactory factory) : IClassFixture<LivingPaperFactory>
{
    [GeneratedRegex(@"<Version>([^<]+)</Version>")]
    private static partial Regex VersionElement();

    private static string ProjectVersion()
    {
        var props = File.ReadAllText(RepositoryFiles.Find("Directory.Build.props"));
        return VersionElement().Match(props).Groups[1].Value;
    }

    private async Task<string> GetAsync(string path, string language)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptLanguage.ParseAdd(language);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TheProject_DefinesAVersion()
    {
        ProjectVersion().ShouldMatch(@"^\d+\.\d+\.\d+$");
    }

    [Theory]
    [InlineData("en", "version")]
    [InlineData("fr", "version")]
    public async Task EveryPage_ShowsTheVersionOfTheProject_InTheLanguageOfTheVisitor(string language, string word)
    {
        var html = await GetAsync("/", language);

        html.ShouldContain($"{word} {ProjectVersion()}");
    }

    [Fact]
    public async Task TheVersion_DoesNotCarryTheBuildMetadata()
    {
        var html = await GetAsync("/", "en");

        html.ShouldNotContain($"{ProjectVersion()}+");
    }
}
