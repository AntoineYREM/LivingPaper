using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The visual identity of the application (PAPER-16, DESIGN.md) checked on the style sheets themselves: contrast is
// computed from the tokens, colors are written once, and nothing is loaded from outside the application.
public partial class DesignSystemTests
{
    private static string CssPath(string name) => RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web", "wwwroot", "css", name));

    private static string[] AllCss => ["tokens.css", "base.css", "components.css", "design.css"];

    private static async Task<string> ReadCssAsync(string name) => await File.ReadAllTextAsync(CssPath(name), TestContext.Current.CancellationToken);

    // The custom properties declared in tokens.css, by name without the leading dashes.
    private static async Task<Dictionary<string, string>> ReadTokensAsync()
    {
        var css = StripComments(await ReadCssAsync("tokens.css"));
        return TokenDeclaration().Matches(css).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Trim(), StringComparer.Ordinal);
    }

    private static string StripComments(string css) => CssComment().Replace(css, string.Empty);

    [GeneratedRegex(@"--([a-z0-9-]+)\s*:\s*([^;]+);")]
    private static partial Regex TokenDeclaration();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(|\boklch\(|\blab\(|\bhwb\(")]
    private static partial Regex ColorLiteral();

    // ---- Contrast (WCAG 2.x) ----

    private static double Luminance(string hex)
    {
        hex.ShouldMatch("^#[0-9a-fA-F]{6}$", "a color token is written as #rrggbb so that its contrast can be checked");
        double Channel(int start)
        {
            var value = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
    }

    private static double Contrast(string foreground, string background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    // Each pair is a text color on the ground it is used on, with the minimum ratio it must reach.
    // 4.5 is WCAG AA for text; 7 is AAA, which body text and controls reach; 3 is AA for the border of a control.
    public static TheoryData<string, string, double> ContrastPairs => new()
    {
        { "color-ink", "color-paper", 7 },
        { "color-ink-soft", "color-paper", 7 },
        { "color-ink-faint", "color-paper", 4.5 },
        { "color-ink", "color-wash", 7 },
        { "color-ink-faint", "color-wash", 4.5 },
        { "color-paper", "color-ink", 7 },
        { "color-error", "color-paper", 4.5 },
        { "color-error", "color-error-wash", 4.5 },
        { "color-success", "color-paper", 4.5 },
        { "color-success", "color-success-wash", 4.5 },
        { "color-warning", "color-paper", 4.5 },
        { "color-warning", "color-warning-wash", 4.5 },
        { "color-ink-soft", "color-wash", 7 },
        { "color-rule-strong", "color-paper", 3 },
    };

    [Theory]
    [MemberData(nameof(ContrastPairs))]
    public async Task EveryTextAndControlColor_MeetsItsContrastRatio_OnItsGround(string foreground, string background, double minimum)
    {
        var tokens = await ReadTokensAsync();

        tokens.ShouldContainKey(foreground);
        tokens.ShouldContainKey(background);
        Contrast(tokens[foreground], tokens[background]).ShouldBeGreaterThanOrEqualTo(minimum, $"{foreground} on {background}");
    }

    [Fact]
    public async Task ThePaperIsWhite_AndTheInkIsBlackOrNearlyBlack()
    {
        var tokens = await ReadTokensAsync();

        tokens["color-paper"].ShouldBe("#ffffff", StringCompareShould.IgnoreCase); // The owner's decision: black on white.
        Contrast(tokens["color-ink"], "#000000").ShouldBeLessThan(1.5, "the ink is black or very nearly");
    }

    // ---- Written once ----

    [Fact]
    public async Task ColorsAreWrittenOnlyInTheTokens()
    {
        foreach (var name in AllCss.Where(name => name != "tokens.css"))
        {
            var css = StripComments(await ReadCssAsync(name));
            ColorLiteral().Matches(css).Select(match => match.Value).ShouldBeEmpty($"{name} must use the color tokens, not a color value");
        }
    }

    [Fact]
    public async Task EveryTokenUsedInTheStyleSheets_IsDefined()
    {
        var tokens = await ReadTokensAsync();
        var used = new HashSet<string>();
        foreach (var name in AllCss)
        {
            foreach (Match match in Regex.Matches(StripComments(await ReadCssAsync(name)), @"var\(--([a-z0-9-]+)"))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        used.Where(token => !tokens.ContainsKey(token)).ShouldBeEmpty("every var(--token) must be declared in tokens.css");
    }

    [Fact]
    public async Task EveryColorTokenIsUsed_SoNoDeadColorStaysInTheSystem()
    {
        var tokens = await ReadTokensAsync();
        var all = string.Concat(await Task.WhenAll(AllCss.Select(ReadCssAsync)));

        tokens.Keys.Where(token => token.StartsWith("color-", StringComparison.Ordinal))
            .Where(token => !all.Contains($"var(--{token})", StringComparison.Ordinal))
            .ShouldBeEmpty("a color that nothing uses should be removed from tokens.css");
    }

    // ---- Typography ----

    [Fact]
    public async Task TheReadingMeasure_IsBetween60And75Characters()
    {
        var measure = (await ReadTokensAsync())["measure"];

        measure.ShouldMatch(@"^\d+ch$");
        int.Parse(measure.AsSpan(0, measure.Length - 2), CultureInfo.InvariantCulture).ShouldBeInRange(60, 75);
    }

    // ---- Nothing from outside, nothing that moves ----

    [Fact]
    public async Task TheStyleSheets_LoadNothingFromOutsideTheApplication()
    {
        foreach (var name in AllCss)
        {
            var css = await ReadCssAsync(name);
            css.ShouldNotContain("http://", Case.Insensitive, name);
            css.ShouldNotContain("https://", Case.Insensitive, name);
            css.ShouldNotContain("@import", Case.Insensitive, name);
        }
    }

    [Fact]
    public async Task EveryFontTheStyleSheetsDeclare_IsServedByTheApplication_WithItsLicense()
    {
        var css = await ReadCssAsync("base.css");
        // The style sheet is in /css, so "../fonts/x.woff2" is /fonts/x.woff2 (it works under a path prefix too).
        var urls = Regex.Matches(css, @"url\(\s*[""']?\.\./fonts/([^""')\s]+)[""']?\s*\)").Select(match => match.Groups[1].Value).Distinct().ToList();

        urls.ShouldNotBeEmpty();
        foreach (var name in urls)
        {
            File.Exists(RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web", "wwwroot", "fonts", name))).ShouldBeTrue(name);
        }

        File.Exists(RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web", "wwwroot", "fonts", "OFL.txt"))).ShouldBeTrue();
    }

    [Fact]
    public async Task NoDecorativeAnimation_AndTheReducedMotionPreferenceIsRespected()
    {
        var all = string.Concat(await Task.WhenAll(AllCss.Select(ReadCssAsync)));

        all.ShouldNotContain("@keyframes", Case.Insensitive);
        all.ShouldContain("prefers-reduced-motion: reduce");
    }

    [Fact]
    public async Task TheStyleSheets_DoNotDependOnBootstrap()
    {
        var all = string.Concat(await Task.WhenAll(AllCss.Select(ReadCssAsync)));

        all.ShouldNotContain("bootstrap", Case.Insensitive);
        Directory.GetFiles(RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web", "wwwroot")), "*bootstrap*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheFocusIndicatorIsNeverRemovedWithoutAReplacement()
    {
        var all = StripComments(string.Concat(await Task.WhenAll(AllCss.Select(ReadCssAsync))));

        // "outline: none" is only acceptable on :focus when :focus-visible draws the ring: the rule below must exist.
        all.ShouldContain(":focus-visible");
        Regex.Count(all, @"outline\s*:\s*(none|0)\b").ShouldBe(0, "never remove the outline: restyle it");
    }
}
