using System.Diagnostics;
using LivingPaper.Application;
using LivingPaper.Web;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// A conversation with a character is one trace in Langfuse: the page invents an id, sends it with every message, and each message
// becomes a span of that trace (PAPER-75).
public class ChatTelemetryTests : IDisposable
{
    private const string Conversation = "0123456789abcdef0123456789abcdef";
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = source => source.Name == LlmTelemetry.SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public ChatTelemetryTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose()
    {
        _listener.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TwoMessagesOfTheSameConversation_ShareOneTrace()
    {
        using var first = ChatTelemetry.StartMessage(Conversation, "cyrano", 3);
        var firstTrace = first!.TraceId;
        first.Stop();
        using var second = ChatTelemetry.StartMessage(Conversation, "cyrano", 3);

        firstTrace.ToHexString().ShouldBe(Conversation);
        second!.TraceId.ToHexString().ShouldBe(Conversation);
    }

    [Fact]
    public void TheMessageCarriesTheNameOfTheTraceTheSessionAndIdsOnly()
    {
        using var carrier = ChatTelemetry.StartMessage(Conversation, "cyrano", 3);

        carrier!.GetBaggageItem("langfuse.trace.name").ShouldBe("character-chat");
        carrier.GetBaggageItem("langfuse.session.id").ShouldBe(Conversation);
        carrier.GetBaggageItem("book.id").ShouldBe("cyrano");
        carrier.GetBaggageItem("character.position").ShouldBe("3");
        carrier.Baggage.Count().ShouldBe(4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("0123456789abcdef0123456789abcdef00")]
    public void AnInvalidOrMissingId_StartsATraceOfItsOwn_WithoutSession(string? conversationId)
    {
        using var carrier = ChatTelemetry.StartMessage(conversationId, "cyrano", 1);

        carrier.ShouldNotBeNull();
        carrier.TraceId.ToHexString().ShouldNotBe(Conversation);
        carrier.GetBaggageItem("langfuse.session.id").ShouldBeNull();
    }

    [Fact]
    public void ACallInsideTheMessage_IsASpanOfTheConversationTrace()
    {
        using var carrier = ChatTelemetry.StartMessage(Conversation, "cyrano", 1);
        using var call = LlmTelemetry.Source.StartActivity("chat-reply");

        call!.TraceId.ShouldBe(carrier!.TraceId);
        call.ParentSpanId.ShouldBe(carrier.SpanId);
        call.GetBaggageItem("langfuse.session.id").ShouldBe(Conversation);
    }
}
