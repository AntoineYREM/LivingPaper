using LivingPaper.Application;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LivingPaper.Web;

// The wiring of OpenTelemetry in the composition root: an exporter for the activities of LlmTelemetry.Source, only when Langfuse is
// configured. Without it nothing is added, and the activities are never created.
internal static class LangfuseTracing
{
    public static IServiceCollection AddLangfuseTracing(this IServiceCollection services, LangfuseOptions options)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("livingpaper", serviceVersion: AppVersion.Current))
            .WithTracing(tracing => tracing
                .AddSource(LlmTelemetry.SourceName)
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = options.Endpoint;
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;

                    // x-langfuse-ingestion-version 4: without it Langfuse takes up to ten minutes to show a trace.
                    exporter.Headers = $"Authorization={options.AuthorizationHeader},x-langfuse-ingestion-version=4";
                }));
        return services;
    }
}
