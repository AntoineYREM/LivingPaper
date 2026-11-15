using System.Globalization;

namespace LivingPaper.Web.Localization;

// Which language a visitor gets: the one they chose (the cookie), else the first one of their browser's list that we offer
// (Accept-Language, regional variants included: "fr-CA" is French), else English.
public static class LanguageResolver
{
    // A browser sends a few hundred bytes. Anything longer is read only up to here, so a hostile header costs nothing.
    private const int MaxHeaderLength = 2048;

    // Among every language that has a translation file.
    public static string Resolve(string? cookieValue, string? acceptLanguage) =>
        Resolve(cookieValue, acceptLanguage, OfferedLanguages.Parse(string.Join(',', SupportedLanguages.All.Select(language => language.Code))));

    // Among the languages the site offers (the LANGUAGES setting): a cookie or a browser asking for another one is ignored.
    public static string Resolve(string? cookieValue, string? acceptLanguage, OfferedLanguages offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        return Resolve(cookieValue, acceptLanguage, offered.TryFind);
    }

    private delegate bool Find(string? code, out LanguageInfo language);

    private static string Resolve(string? cookieValue, string? acceptLanguage, Find find)
    {
        if (find(cookieValue, out var chosen))
        {
            return chosen.Code;
        }

        return FromBrowser(acceptLanguage, find) ?? SupportedLanguages.Default.Code;
    }

    private static string? FromBrowser(string? header, Find find)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var text = header.Length > MaxHeaderLength ? header[..MaxHeaderLength] : header;
        var best = new List<(double Quality, int Position, string Code)>();
        var position = 0;
        foreach (var entry in text.Split(','))
        {
            position++;
            if (TryRead(entry, out var tag, out var quality)
                && find(tag.Split('-')[0], out var language))
            {
                best.Add((quality, position, language.Code));
            }
        }

        return best.Count == 0 ? null : best.OrderByDescending(item => item.Quality).ThenBy(item => item.Position).First().Code;
    }

    // "fr-CA;q=0.8" is the tag "fr-CA" with a quality of 0.8. A tag without quality has 1. An entry the browser refuses (q=0),
    // a wildcard and anything unreadable are not usable.
    private static bool TryRead(string entry, out string tag, out double quality)
    {
        tag = string.Empty;
        quality = 1;
        var parts = entry.Split(';');
        tag = parts[0].Trim();
        if (tag.Length == 0 || tag == "*")
        {
            return false;
        }

        foreach (var parameter in parts.Skip(1).Select(part => part.Trim()))
        {
            if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(parameter[2..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quality) || quality is <= 0 or > 1)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
