using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LivingPaper.Web;

// For a page that receives a file. A request larger than the page accepts cannot be read: the visitor would get a bare error
// of the server, or a broken connection. This filter sends them back to the form instead, which says why (?refused=tooLarge).
// The rest of the upload is first read and thrown away, never kept in memory nor on the disk, because a browser only shows
// the answer once it has sent its file. Nothing else is read or changed, so no antiforgery token is needed for that.
[AttributeUsage(AttributeTargets.Class)]
public sealed class RefuseOversizedUploadAttribute(long maxRequestBytes) : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
    // The value of "refused" in the address of the form.
    public const string TooLarge = "tooLarge";

    // The most that is read to be thrown away. Beyond, it is not worth waiting: the connection may then be cut.
    private const long MaxDiscardedBytes = 1024L * 1024 * 1024;

    // After the limit of the size of the request is set (900), which this lifts to read the rest, and before the antiforgery
    // check (1000), which reads the form.
    public int Order => 950;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method) || request.ContentLength is not { } length || length <= maxRequestBytes)
        {
            return;
        }

        if (length <= MaxDiscardedBytes)
        {
            if (context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = MaxDiscardedBytes;
            }

            await request.Body.CopyToAsync(Stream.Null, context.HttpContext.RequestAborted);
        }

        context.Result = new LocalRedirectResult($"{request.PathBase}{request.Path}?refused={TooLarge}");
    }
}
