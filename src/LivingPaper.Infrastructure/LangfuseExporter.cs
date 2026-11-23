using LivingPaper.Application;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;

namespace LivingPaper.Infrastructure;

// The OpenTelemetry exporter to Langfuse, shared by the web application (through its host) and the evaluation tool (through
// Sdk.CreateTracerProviderBuilder): the activities of LlmTelemetry.Source, sent over OTLP/HTTP protobuf with the keys of the project.
public static class LangfuseExporter
{
    public static TracerProviderBuilder AddLangfuseExporter(this TracerProviderBuilder tracing, LangfuseOptions options)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(options);
        return tracing

            // Not the default parent-based sampler: ASP.NET Core opens an unsampled activity for every request, and under it the
            // activity of a chat reply would be dropped.
            .SetSampler(new AlwaysOnSampler())
            .AddSource(LlmTelemetry.SourceName)
            .AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = options.Endpoint;
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;

                // x-langfuse-ingestion-version 4: without it Langfuse takes up to ten minutes to show a trace.
                exporter.Headers = $"Authorization={options.AuthorizationHeader},x-langfuse-ingestion-version=4";
            });
    }
}
