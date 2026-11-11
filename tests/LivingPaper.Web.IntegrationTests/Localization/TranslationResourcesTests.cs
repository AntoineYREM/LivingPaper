using System.Text.RegularExpressions;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests.Localization;

// The real translation files, and the code that uses them (PAPER-13): a language can never miss a key or a variable,
// and a key used in a view or in the code must exist. These tests are why a missing translation is caught at build time.
public partial class TranslationResourcesTests
{
    private static readonly string[] PluralForms = [".one", ".other"];

    private static string WebProject => RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web"));

    private static IReadOnlyDictionary<string, string> Load(string language) =>
        TranslationFile.Parse(File.ReadAllText(Path.Combine(WebProject, "Resources", language + ".json")), language + ".json");

    private static IEnumerable<string> SourceFiles() => Directory
        .EnumerateFiles(WebProject, "*.*", SearchOption.AllDirectories)
        .Where(path => path.EndsWith(".cshtml", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                       && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Where(path => !IsDevelopmentOnly(path));

    // The design reference page is served in development only and shows sample content: it has no translation.
    private static bool IsDevelopmentOnly(string path) => Path.GetFileName(path).StartsWith("Design.cshtml", StringComparison.Ordinal);

    private static string Relative(string path) => Path.GetRelativePath(WebProject, path);

    // ---- The files ----

    [Fact]
    public void EveryLanguageFile_IsValid()
    {
        foreach (var language in SupportedLanguages.All)
        {
            Should.NotThrow(() => Load(language.Code), $"{language.Code}.json");
        }
    }

    [Fact]
    public void EverySupportedLanguage_HasItsFile_AndEveryFileIsASupportedLanguage()
    {
        var files = Directory.GetFiles(Path.Combine(WebProject, "Resources"), "*.json").Select(path => Path.GetFileNameWithoutExtension(path)).Order().ToList();

        // A failure here means: add a language to SupportedLanguages when you add its file, and the other way round.
        files.ShouldBe(SupportedLanguages.All.Select(language => language.Code).Order());
    }

    [Fact]
    public void EveryLanguage_HasExactlyTheKeysOfEnglish()
    {
        var english = Load("en").Keys.ToHashSet();

        foreach (var language in SupportedLanguages.All.Where(language => language.Code != "en"))
        {
            var keys = Load(language.Code).Keys.ToHashSet();

            english.Except(keys).Order().ShouldBeEmpty($"{language.Code}.json lacks keys that en.json has");
            keys.Except(english).Order().ShouldBeEmpty($"{language.Code}.json has keys that en.json does not have");
        }
    }

    [Fact]
    public void EveryText_HasTheSameVariablesInEveryLanguage()
    {
        var english = Load("en");

        foreach (var language in SupportedLanguages.All.Where(language => language.Code != "en"))
        {
            var texts = Load(language.Code);
            foreach (var (key, englishText) in english.Where(pair => texts.ContainsKey(pair.Key)))
            {
                VariablesOf(texts[key]).ShouldBe(VariablesOf(englishText), $"{language.Code}.json, key {key}: the {{variables}} must be the same as in en.json");
            }
        }
    }

    [Fact]
    public void EveryPluralKey_HasItsTwoForms_InEveryLanguage()
    {
        foreach (var language in SupportedLanguages.All)
        {
            var keys = Load(language.Code).Keys.ToHashSet();

            foreach (var key in keys.Where(key => key.EndsWith(".one", StringComparison.Ordinal)))
            {
                keys.ShouldContain(key[..^4] + ".other", $"{language.Code}.json: {key} has no .other form");
            }

            foreach (var key in keys.Where(key => key.EndsWith(".other", StringComparison.Ordinal)))
            {
                keys.ShouldContain(key[..^6] + ".one", $"{language.Code}.json: {key} has no .one form");
            }
        }
    }

    [Fact]
    public void TheTextsOfEnglishAndFrench_AreNotTheSame_SoNothingWasLeftUntranslated()
    {
        var english = Load("en");
        var french = Load("fr");

        // A name or a brand may be the same in both languages; a sentence may not.
        english.Where(pair => french[pair.Key] == pair.Value && pair.Value.Contains(' ', StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ShouldBeEmpty("these sentences are identical in en.json and fr.json: translate them");
    }

    private static List<string> VariablesOf(string text) => [.. PromptTemplate.Parse(text).VariableNames.Order()];

    // ---- The code that uses them ----

    // Views and code read a text as T["key"], T.Format("key", ...) or T.Plural("key", ...); in a class that injects the
    // localizer under another name, texts["key"] and localizer["key"] are found too.
    [GeneratedRegex(@"\b(?:T|texts|localizer)\[\s*""([^""]+)""\s*\]")]
    private static partial Regex IndexerUse();

    [GeneratedRegex(@"\b(?:T|texts|localizer)\.Format\(\s*""([^""]+)""")]
    private static partial Regex FormatUse();

    [GeneratedRegex(@"\b(?:T|texts|localizer)\.Plural\(\s*""([^""]+)""")]
    private static partial Regex PluralUse();

    [GeneratedRegex(@"//[^
]*")]
    private static partial Regex LineComment();

    // Comments show examples ("T.Format(\"key\", ...)") that are not keys of the application.
    private static string WithoutComments(string source) => LineComment().Replace(RazorComment().Replace(source, string.Empty), string.Empty);

    [Fact]
    public void EveryKeyUsedInAViewOrInTheCode_ExistsInEnglish()
    {
        var english = Load("en");
        var missing = new List<string>();
        var used = 0;

        foreach (var path in SourceFiles())
        {
            var source = WithoutComments(File.ReadAllText(path));
            foreach (var key in IndexerUse().Matches(source).Concat(FormatUse().Matches(source)).Select(match => match.Groups[1].Value))
            {
                used++;
                if (!english.ContainsKey(key))
                {
                    missing.Add($"{Relative(path)}: {key}");
                }
            }

            foreach (var key in PluralUse().Matches(source).Select(match => match.Groups[1].Value))
            {
                used++;
                foreach (var form in PluralForms.Where(form => !english.ContainsKey(key + form)))
                {
                    missing.Add($"{Relative(path)}: {key}{form}");
                }
            }
        }

        used.ShouldBeGreaterThan(3, "no key was found in the views: the pattern that looks for them no longer matches the code");
        missing.ShouldBeEmpty("these keys are used but are not in Resources/en.json");
    }

    // ---- No text written in a view ----

    [GeneratedRegex(@"@\*.*?\*@", RegexOptions.Singleline)]
    private static partial Regex RazorComment();

    [GeneratedRegex(@">([^<>]*)<")]
    private static partial Regex TextBetweenTags();

    [GeneratedRegex(@"\b(?:aria-label|title|alt|placeholder|aria-description)=""([^""@]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex TextAttribute();

    // A coarse reading of a Razor file: a code block ("@{ ... }") is code, everything else is markup. The text of a tag is what
    // the user reads: any letter there that does not come from an @expression is a text that belongs in a translation file.
    // (An "@if" or "@foreach" line between two tags contains an "@", so it is not mistaken for a text.)
    private static string WithoutRazorCode(string source)
    {
        var text = RazorComment().Replace(source, string.Empty);
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '@' && index + 1 < text.Length && text[index + 1] == '{')
            {
                var depth = 0;
                var close = index + 1;
                for (; close < text.Length; close++)
                {
                    depth += text[close] == '{' ? 1 : text[close] == '}' ? -1 : 0;
                    if (depth == 0)
                    {
                        break;
                    }
                }

                index = close;
                continue;
            }

            result.Append(text[index]);
        }

        return result.ToString();
    }

    [Fact]
    public void NoViewHoldsAnUserFacingTextWrittenInIt()
    {
        var violations = new List<string>();
        var views = SourceFiles().Where(path => path.EndsWith(".cshtml", StringComparison.Ordinal)).ToList();

        views.ShouldNotBeEmpty();
        foreach (var path in views)
        {
            var source = WithoutRazorCode(File.ReadAllText(path));
            foreach (Match match in TextBetweenTags().Matches(source))
            {
                var text = match.Groups[1].Value.Trim();
                if (text.Length > 0 && !text.Contains('@', StringComparison.Ordinal) && text.Any(char.IsLetter))
                {
                    violations.Add($"{Relative(path)}: \"{text}\"");
                }
            }

            foreach (Match match in TextAttribute().Matches(source))
            {
                if (match.Groups[1].Value.Any(char.IsLetter))
                {
                    violations.Add($"{Relative(path)}: {match.Value}");
                }
            }
        }

        violations.ShouldBeEmpty("a text visible to the user must come from a translation file: T[\"key\"]");
    }

    [Fact]
    public void ThePageTextsOfAViewThatDoesNotBelongToDevelopment_AreFoundByTheScan()
    {
        // The scan must not be blind: a view with a written text is caught.
        var source = "<main><h1>Written in the view</h1><p>@T[\"a\"]</p><a aria-label=\"Also written\">@T[\"b\"]</a></main>";

        TextBetweenTags().Matches(WithoutRazorCode(source)).Select(match => match.Groups[1].Value.Trim()).ShouldContain("Written in the view");
        TextAttribute().Matches(source).Select(match => match.Groups[1].Value).ShouldContain("Also written");
    }
}
