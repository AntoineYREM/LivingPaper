using System.Diagnostics;
using System.Globalization;
using LivingPaper.Application;

namespace LivingPaper.Web;

// The tracing of the chat (PAPER-75). Nothing about a conversation is stored, but the page invents an id for the one it shows and
// sends it with every message: it becomes the trace id (and the Langfuse session), so all the messages of a conversation land in one
// trace, each model call being a "chat-reply" span of it. Only ids and numbers are recorded, never a name or a text.
public static class ChatTelemetry
{
    // Returns the activity that carries the trace for the duration of a message (dispose it at the end), or null when tracing is off.
    // It is exported as the "chat-message" span and holds the baggage, which TracingChatModel copies on the call's span.
    public static Activity? StartMessage(string? conversationId, string bookId, int characterPosition)
    {
        if (!LlmTelemetry.Source.HasListeners())
        {
            return null;
        }

        // An invalid or missing id gets a trace of its own: a conversation is not a child of the HTTP request that carries a message.
        var valid = TryReadTraceId(conversationId, out var traceId);
        if (!valid)
        {
            traceId = ActivityTraceId.CreateRandom();
        }

        // A real span of the source, so it is exported: it is the node under which the model calls of the message appear in Langfuse.
        var parent = new ActivityContext(traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var carrier = LlmTelemetry.Source.StartActivity("chat-message", ActivityKind.Internal, parent);
        if (carrier is null)
        {
            return null;
        }

        carrier.SetTag("langfuse.observation.type", "span");
        carrier.SetTag("langfuse.trace.name", "character-chat");
        carrier.SetTag("book.id", bookId);
        carrier.SetTag("character.position", characterPosition.ToString(CultureInfo.InvariantCulture));
        carrier.AddBaggage("langfuse.trace.name", "character-chat");
        carrier.AddBaggage("book.id", bookId);
        carrier.AddBaggage("character.position", characterPosition.ToString(CultureInfo.InvariantCulture));
        if (valid)
        {
            carrier.SetTag("langfuse.session.id", conversationId);
            carrier.AddBaggage("langfuse.session.id", conversationId);
        }

        return carrier;
    }

    // 32 lowercase hexadecimal digits, not all zeros: what the page generates. Anything else is ignored, never trusted.
    private static bool TryReadTraceId(string? text, out ActivityTraceId traceId)
    {
        traceId = default;
        if (text is not { Length: 32 } || !text.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')) || text.All(c => c == '0'))
        {
            return false;
        }

        traceId = ActivityTraceId.CreateFromString(text);
        return true;
    }
}
