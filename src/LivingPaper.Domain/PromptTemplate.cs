using System.Text.RegularExpressions;

namespace LivingPaper.Domain;

// The text of a prompt with {variable} placeholders. A variable is a name made of letters and digits, starting with a
// letter, between braces: {bookTitle}. Anything else between braces is plain text, so a prompt can show a JSON example
// ({"characters":[...]}) without escaping, and stays easy to edit by hand.
public sealed partial class PromptTemplate
{
    private PromptTemplate(string text, IReadOnlyList<string> variableNames)
    {
        Text = text;
        VariableNames = variableNames;
    }

    public string Text { get; }

    // Each variable once, in order of first appearance.
    public IReadOnlyList<string> VariableNames { get; }

    public static PromptTemplate Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var names = VariablePattern().Matches(text).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        return new PromptTemplate(text, names);
    }

    // The variables used in the template that are not in the allowed list (names are case sensitive).
    public IReadOnlyList<string> FindUnknownVariables(IEnumerable<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        var known = allowed.ToHashSet(StringComparer.Ordinal);
        return [.. VariableNames.Where(name => !known.Contains(name))];
    }

    // One pass over the template: a value is inserted as it is and never searched again for placeholders, so a value
    // that contains "{profile}" cannot inject another variable. The exception names a missing variable, never a value.
    public string Render(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return VariablePattern().Replace(Text, match =>
        {
            var name = match.Groups[1].Value;
            return values.TryGetValue(name, out var value)
                ? value
                : throw new ArgumentException($"No value was given for the variable {{{name}}}.", nameof(values));
        });
    }

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
