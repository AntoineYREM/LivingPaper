namespace LivingPaper.Web.Localization;

// The texts of the interface in the language of the request. In a view it is T: @T["home.tagline"], @T.Format("key", new { name }),
// @T.Plural("book.characters", count). A text is plain text: Razor encodes it, never takes it as HTML.
public interface ITextLocalizer
{
    // The language in use ("en" or "fr").
    string Language { get; }

    // The text of a key. When the language lacks it, the English text is used and the gap is logged; when no language has
    // it, the key itself is shown and an error is logged: nothing is ever missing silently. A test fails for a key used in
    // the code and absent from en.json, so this is for the cases it cannot see.
    string this[string key] { get; }

    // The text with its {variables} replaced by the properties of an object: Format("greeting", new { name = "Argan" }).
    string Format(string key, object values);

    // The plural form for a number, from the keys "<key>.one" and "<key>.other", with {count} written the way the language
    // writes numbers. English uses "one" for 1; French uses it for 0 and 1. Other variables can be given too.
    string Plural(string key, long count, object? values = null);
}
