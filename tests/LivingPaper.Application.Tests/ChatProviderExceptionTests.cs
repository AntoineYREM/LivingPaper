using LivingPaper.Application;
using Shouldly;

namespace LivingPaper.Application.Tests;

public class ChatProviderExceptionTests
{
    [Theory]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(599, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(422, false)]
    [InlineData(301, false)]
    [InlineData(302, false)]
    public void IsTransient_ForAnHttpStatus_FollowsTheRetryRules(int status, bool expected)
    {
        new ChatProviderException(ChatFailureKind.Http, "message", status).IsTransient.ShouldBe(expected);
    }

    [Theory]
    [InlineData(ChatFailureKind.Unreachable, true)]
    [InlineData(ChatFailureKind.TimedOut, true)]
    [InlineData(ChatFailureKind.StreamCut, true)]
    [InlineData(ChatFailureKind.EmptyReply, true)]
    [InlineData(ChatFailureKind.InvalidResponse, false)]
    [InlineData(ChatFailureKind.ContentFiltered, false)]
    public void IsTransient_ForOtherFailures_FollowsTheRetryRules(ChatFailureKind kind, bool expected)
    {
        new ChatProviderException(kind, "message").IsTransient.ShouldBe(expected);
    }

    [Fact]
    public void IsTransient_IsFalse_ForAnHttpFailureWithoutAStatus()
    {
        new ChatProviderException(ChatFailureKind.Http, "message").IsTransient.ShouldBeFalse();
    }

    [Fact]
    public void ContentFilteredException_IsAChatProviderException_NeverTransient()
    {
        var exception = new ContentFilteredException();

        exception.ShouldBeAssignableTo<ChatProviderException>();
        exception.Kind.ShouldBe(ChatFailureKind.ContentFiltered);
        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Exception_KeepsTheKindAndTheStatus()
    {
        var exception = new ChatProviderException(ChatFailureKind.Http, "The provider answered HTTP 401.", 401);

        exception.Kind.ShouldBe(ChatFailureKind.Http);
        exception.StatusCode.ShouldBe(401);
        exception.Message.ShouldBe("The provider answered HTTP 401.");
    }
}
