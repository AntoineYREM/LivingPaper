using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace LivingPaper.Domain;

// A book's identifier, and its address (/books/cyrano-de-bergerac). It is the name of the book's file (data/books/{id}.json) and can
// come from a URL or an import, so the only accepted form is a slug: lowercase letters and digits separated by single hyphens,
// 80 characters at most. No path, no separator, no dot, no extension. Before 0.3.0 an id was 32 hexadecimal characters; that form is
// still a valid slug, so older files and exports are still read.
public readonly record struct BookId
{
    private const int MaxLength = 80;
    private const int MaxTitleSlugLength = 60;

    private readonly string? _value;

    private BookId(string value) => _value = value;

    // The old random form, kept for books that were created before the slugs. New books get FromTitle.
    public static BookId New() => new(Guid.NewGuid().ToString("N"));

    // The slug of the title, made unique by a number (-2, -3, ...) when isTaken says it is already used.
    public static BookId FromTitle(string title, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(isTaken);
        var slug = Slugify(title);
        if (!isTaken(slug))
        {
            return new BookId(slug);
        }

        for (var number = 2; ; number++)
        {
            var suffix = "-" + number.ToString(CultureInfo.InvariantCulture);
            var candidate = slug[..Math.Min(slug.Length, MaxLength - suffix.Length)].TrimEnd('-') + suffix;
            if (!isTaken(candidate))
            {
                return new BookId(candidate);
            }
        }
    }

    public static bool TryParse([NotNullWhen(true)] string? text, out BookId id)
    {
        id = default;
        if (text is not { Length: > 0 and <= MaxLength } || !IsSlug(text) || IsZeroGuid(text))
        {
            return false;
        }

        id = new BookId(text);
        return true;
    }

    // True for an identifier made of the 32 hexadecimal characters of the versions before the slugs: such a book is given a slug when it is
    // migrated or imported.
    public bool IsOldIdentifier => _value is { Length: 32 } && Guid.TryParseExact(_value, "N", out _);

    public override string ToString() => _value ?? string.Empty;

    // Letters and digits of the title, accents removed, anything else a hyphen, never two in a row. "book" when nothing is left.
    private static string Slugify(string title)
    {
        var slug = new StringBuilder();
        foreach (var character in (title ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(character);
            }
            else if (character is >= 'A' and <= 'Z')
            {
                slug.Append(char.ToLowerInvariant(character));
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
                     && slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var text = slug.ToString();
        if (text.Length > MaxTitleSlugLength)
        {
            text = text[..MaxTitleSlugLength];
        }

        text = text.Trim('-');
        return text.Length == 0 ? "book" : text;
    }

    private static bool IsSlug(string text)
    {
        var previousWasHyphen = true; // a slug cannot start with a hyphen
        foreach (var character in text)
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                previousWasHyphen = false;
            }
            else if (character == '-' && !previousWasHyphen)
            {
                previousWasHyphen = true;
            }
            else
            {
                return false;
            }
        }

        return !previousWasHyphen;
    }

    private static bool IsZeroGuid(string text) => text.Length == 32 && text.All(character => character == '0');
}
