using LivingPaper.Infrastructure;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LivingPaper.Web;

// The wiring of OpenTelemetry in the composition root: the exporter of LangfuseExporter (shared with the evaluation tool), only when
// Langfuse is configured. Without it nothing is added, and the activities are never created.
internal static class LangfuseTracing
{
    public static IServiceCollection AddLangfuseTracing(this IServiceCollection services, LangfuseOptions options)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("livingpaper", serviceVersion: AppVersion.Current))
            .WithTracing(tracing => tracing.AddLangfuseExporter(options));
        return services;
    }
}
