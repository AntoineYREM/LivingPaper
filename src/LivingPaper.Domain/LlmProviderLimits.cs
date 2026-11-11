namespace LivingPaper.Domain;

public static class LlmProviderLimits
{
    public const int Name = 100;
    public const int BaseUrl = 2048;
    public const int ApiKey = 500;
    public const int Model = 200;

    // A single person has a handful of language models; the limit only stops a runaway settings file.
    public const int MaxProviders = 20;
}
