namespace LivingPaper.Web.Localization;

// The language of the request being served. One per request: LanguageMiddleware sets it, TextLocalizer reads it.
public sealed class LanguageContext
{
    public string Code { get; set; } = SupportedLanguages.Default.Code;
}
