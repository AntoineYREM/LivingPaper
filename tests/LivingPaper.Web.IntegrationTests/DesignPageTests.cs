using System.Net;
using System.Text.RegularExpressions;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The design reference page (/Design) shows the visual identity of PAPER-16 with sample content. It is for development
// only: it is never served in production, so its sample texts are not product UI and need no translation.
public partial class DesignPageTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"<input\b[^>]*>|<select\b[^>]*>|<textarea\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex FormControl();

    [GeneratedRegex(@"\bid=""([^""]+)""")]
    private static partial Regex IdAttribute();

    [GeneratedRegex(@"<label\b[^>]*\bfor=""([^""]+)""")]
    private static partial Regex LabelFor();

    [Fact]
    public async Task InDevelopment_ThePageIsServed_WithTheSharedStyleSheets()
    {
        using var factory = new LivingPaperFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/Design", Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("/css/tokens.css");
        html.ShouldContain("/css/base.css");
        html.ShouldContain("/css/components.css");
        html.ShouldContain("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"");
        html.ShouldContain("<html lang=\"en\"");
    }

    [Fact]
    public async Task InProduction_ThePageDoesNotExist()
    {
        using var factory = LivingPaperFactory.With(environment: "Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/Design", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/css/tokens.css", "text/css")]
    [InlineData("/css/base.css", "text/css")]
    [InlineData("/css/components.css", "text/css")]
    [InlineData("/fonts/eb-garamond-latin-wght-normal.woff2", "font/woff2")]
    [InlineData("/fonts/eb-garamond-latin-wght-italic.woff2", "font/woff2")]
    [InlineData("/fonts/OFL.txt", "text/plain")]
    public async Task TheStyleSheetsAndFonts_AreServedByTheApplication(string path, string contentType)
    {
        using var factory = new LivingPaperFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(contentType);
    }

    [Fact]
    public async Task ThePage_LoadsNothingFromOutside_AndNeedsNoJavaScript()
    {
        using var factory = new LivingPaperFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/Design", Token);

        // No script of its own and none from outside: only the flag that tells the style sheets JavaScript runs.
        Regex.Matches(html, @"<script\b[^>]*>", RegexOptions.IgnoreCase).Select(match => match.Value).ShouldAllBe(tag => tag.Contains("src=\"/js/js-flag.js\"", StringComparison.Ordinal));
        Regex.Matches(html, @"\b(src|href)=""https?://", RegexOptions.IgnoreCase).ShouldBeEmpty();
        html.ShouldNotContain("bootstrap", Case.Insensitive);
    }

    [Fact]
    public async Task ThePage_HasOneMainTitle_AMainLandmark_AndASkipLink()
    {
        using var factory = new LivingPaperFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/Design", Token);

        Regex.Count(html, @"<h1\b", RegexOptions.IgnoreCase).ShouldBe(1);
        html.ShouldContain("<main id=\"main\"");
        html.ShouldContain("href=\"#main\"");
    }

    [Fact]
    public async Task EveryFormField_HasAVisibleLabel()
    {
        using var factory = new LivingPaperFactory();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Design", Token);

        var labelled = LabelFor().Matches(html).Select(match => match.Groups[1].Value).ToHashSet();
        var controls = FormControl().Matches(html).Select(match => match.Value)
            .Where(tag => !Regex.IsMatch(tag, @"type=""(hidden|submit|button|radio|checkbox)""", RegexOptions.IgnoreCase))
            .ToList();

        controls.ShouldNotBeEmpty();
        foreach (var control in controls)
        {
            var id = IdAttribute().Match(control).Groups[1].Value;
            id.ShouldNotBeNullOrEmpty($"a field needs an id to be labelled: {control}");
            labelled.ShouldContain(id, $"the field {id} has no <label for>");
        }
    }

    [Fact]
    public async Task EveryImage_HasAnAlternativeText()
    {
        using var factory = new LivingPaperFactory();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Design", Token);

        Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(match => match.Value)
            .Where(tag => !tag.Contains("alt=", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty();
    }
}
