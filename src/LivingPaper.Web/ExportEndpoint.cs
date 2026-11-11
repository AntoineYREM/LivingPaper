using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Web;

// The download of an analyzed book (PAPER-12): GET /Book/{id}/Export. The file is built from the fields of the book (see
// IBookArchive), so it holds no key, no setting and no path of the machine. It is an attachment, whatever the browser would like
// to do with JSON, and is never given a type it could be taken for a page. A book that is not there, or not analyzed yet, is a 404.
// A GET changes nothing, so it needs no antiforgery token.
public static class ExportEndpoint
{
    public static void MapExport(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        routes.MapGet("/Book/{id}/Export", async (string id, BookService books, HttpContext context) =>
        {
            if (!BookId.TryParse(id, out var bookId))
            {
                return Results.NotFound();
            }

            var file = await books.ExportAsync(bookId, context.RequestAborted);
            if (file is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(file.Content, "application/json", file.FileName);
        });
    }
}
