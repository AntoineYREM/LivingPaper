using System.Diagnostics;

namespace LivingPaper.Application;

// The one ActivitySource of the application (System.Diagnostics, part of .NET: no package). The analysis, the connection test
// and the calls to a language model start Activities here. Nothing listens by default, and an Activity that nobody listens to
// costs nothing; the web application adds an exporter (Langfuse, see PAPER-75) only when it is configured.
// An Activity carries ids, names, counts and codes. The text of a book or of a conversation is never put in one, except the
// prompts and replies of the calls, when the user turned LANGFUSE_CAPTURE_CONTENT on (see TracingChatModel).
public static class LlmTelemetry
{
    public const string SourceName = "LivingPaper";

    public static ActivitySource Source { get; } = new(SourceName);
}
