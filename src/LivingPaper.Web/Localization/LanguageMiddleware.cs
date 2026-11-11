namespace LivingPaper.Web.Localization;

// The name of the cookie that remembers the visitor's choice. It only holds a language code: it is strictly functional
// (the site could not honor the choice without it), so it needs no consent banner.
public static class LanguageCookie
{
    public const string Name = "lp_lang";
}

// Decides the language of each request and tells the world about it: the response carries Content-Language, and says it varies
// with the cookie and the browser language, so a shared cache never serves one language to a visitor who asked for another.
public sealed class LanguageMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, LanguageContext language)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(language);

        language.Code = LanguageResolver.Resolve(
            context.Request.Cookies[LanguageCookie.Name],
            context.Request.Headers.AcceptLanguage.ToString());

        context.Response.OnStarting(() =>
        {
            context.Response.Headers.ContentLanguage = language.Code;
            context.Response.Headers.Append("Vary", "Accept-Language");
            context.Response.Headers.Append("Vary", "Cookie");
            return Task.CompletedTask;
        });

        return next(context);
    }
}

public static class LocalizationExtensions
{
    // Loads the embedded translation files now: an invalid file stops the application at start, with its name.
    public static IServiceCollection AddTextLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(TranslationCatalog.LoadEmbedded(typeof(TranslationCatalog).Assembly));
        services.AddScoped<LanguageContext>();
        services.AddScoped<ITextLocalizer, TextLocalizer>();
        return services;
    }

    public static IApplicationBuilder UseTextLocalization(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<LanguageMiddleware>();
    }
}
