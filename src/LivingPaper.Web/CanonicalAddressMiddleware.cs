using System.Text;

namespace LivingPaper.Web;

// One address per page (PAPER-59): the addresses are in lowercase and plural (/books/{id}/characters/{position}/chat). An address
// with capitals, or one of the old singular ones (/Book/{id}), is sent for good (301) to the canonical one, with the query string
// kept. Only reads (GET, HEAD) are redirected: a form posted to an old address is not sent somewhere else.
public sealed class CanonicalAddressMiddleware(RequestDelegate next)
{
    // The segments that were singular before: /Book/{id}/Character/{position} became /books/{id}/characters/{position}.
    private static readonly Dictionary<string, string> Plurals = new(StringComparer.Ordinal)
    {
        ["book"] = "books",
        ["character"] = "characters",
    };

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            var path = context.Request.Path.Value ?? "/";
            var canonical = Canonical(path);
            if (!string.Equals(path, canonical, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                context.Response.Headers.Location = context.Request.PathBase + canonical + context.Request.QueryString;
                return Task.CompletedTask;
            }
        }

        return next(context);
    }

    // The files of wwwroot (a segment with a dot in the last position) keep their name.
    internal static string Canonical(string path)
    {
        var segments = path.Split('/');
        if (segments[^1].Contains('.', StringComparison.Ordinal))
        {
            return path;
        }

        var result = new StringBuilder();
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index].ToLowerInvariant();
            // Only the name of a resource is made plural, never an identifier: /books/book is the book whose id is "book".
            var isName = index == 1 || (index == 3 && segments[1].StartsWith("book", StringComparison.OrdinalIgnoreCase));
            result.Append(index == 0 ? string.Empty : "/").Append(isName && Plurals.TryGetValue(segment, out var plural) ? plural : segment);
        }

        return result.ToString();
    }
}
