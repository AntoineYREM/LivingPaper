using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Domain.Tests;

public class LlmProviderValidatorTests
{
    private static LlmProviderDraft Valid() => new("OpenAI", "https://api.openai.com/v1", "sk-secret", "gpt-5-nano");

    private static IReadOnlyList<FieldError> Validate(LlmProviderDraft draft, params string[] existingNames) =>
        LlmProviderValidator.Validate(draft, existingNames);

    [Fact]
    public void AValidDraft_HasNoError()
    {
        Validate(Valid()).ShouldBeEmpty();
    }

    [Fact]
    public void AnEmptyKey_IsAccepted_ForLocalServersThatNeedNone()
    {
        Validate(Valid() with { ApiKey = string.Empty }).ShouldBeEmpty();
        Validate(Valid() with { ApiKey = null }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:1234/v1")]
    [InlineData("http://192.168.1.20:8080/v1")]
    [InlineData("http://host.docker.internal:11434/v1")]
    [InlineData("https://api.openai.com/v1/")]
    [InlineData("  https://api.openai.com/v1  ")]
    public void PrivateAndPublicHttpAddresses_AreAccepted(string url)
    {
        // Private addresses are allowed on purpose: local models (Ollama, LM Studio) must work.
        Validate(Valid() with { BaseUrl = url }).ShouldBeEmpty();
    }

    // ---- Name ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TheName_IsRequired(string? name)
    {
        Validate(Valid() with { Name = name }).ShouldBe([new FieldError("name", LlmProviderErrors.Required)]);
    }

    [Fact]
    public void TheName_IsLimitedInLength_AfterTrimming()
    {
        Validate(Valid() with { Name = new string('n', LlmProviderLimits.Name) }).ShouldBeEmpty();
        Validate(Valid() with { Name = "  " + new string('n', LlmProviderLimits.Name) + "  " }).ShouldBeEmpty();
        Validate(Valid() with { Name = new string('n', LlmProviderLimits.Name + 1) }).ShouldBe([new FieldError("name", LlmProviderErrors.TooLong)]);
    }

    [Theory]
    [InlineData("OpenAI", "OpenAI")]
    [InlineData("openai", "OpenAI")]
    [InlineData("  OPENAI ", "OpenAI")]
    public void TheName_MustBeDifferentFromTheOthers_IgnoringCaseAndSpaces(string name, string existing)
    {
        Validate(Valid() with { Name = name }, existing).ShouldBe([new FieldError("name", LlmProviderErrors.Taken)]);
    }

    [Fact]
    public void TheName_CanResembleAnotherOne_WithoutBeingTheSame()
    {
        Validate(Valid() with { Name = "OpenAI 2" }, "OpenAI").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Open\nAI")]
    [InlineData("Open\tAI")]
    [InlineData("Open\u0000AI")]
    public void TheName_CannotContainControlCharacters(string name)
    {
        Validate(Valid() with { Name = name }).ShouldBe([new FieldError("name", LlmProviderErrors.InvalidCharacters)]);
    }

    // ---- Base URL ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TheBaseUrl_IsRequired(string? url)
    {
        Validate(Valid() with { BaseUrl = url }).ShouldBe([new FieldError("baseUrl", LlmProviderErrors.Required)]);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("api.openai.com/v1")]
    [InlineData("/v1")]
    [InlineData("http://")]
    [InlineData("https:///v1")]
    public void TheBaseUrl_MustBeAnAbsoluteUrl(string url)
    {
        Validate(Valid() with { BaseUrl = url }).ShouldBe([new FieldError("baseUrl", LlmProviderErrors.Invalid)]);
    }

    [Theory]
    [InlineData("ftp://example.com/v1")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("gopher://example.com")]
    [InlineData("ws://example.com/v1")]
    public void TheBaseUrl_MustBeHttpOrHttps(string url)
    {
        Validate(Valid() with { BaseUrl = url }).ShouldBe([new FieldError("baseUrl", LlmProviderErrors.Scheme)]);
    }

    [Theory]
    [InlineData("https://user:password@example.com/v1")]
    [InlineData("https://user@example.com/v1")]
    public void TheBaseUrl_CannotCarryCredentials_BecauseItIsStoredInClear(string url)
    {
        Validate(Valid() with { BaseUrl = url }).ShouldBe([new FieldError("baseUrl", LlmProviderErrors.Credentials)]);
    }

    [Theory]
    [InlineData("https://example.com/v1?api-key=secret")]
    [InlineData("https://example.com/v1?x=1")]
    [InlineData("https://example.com/v1#frag")]
    public void TheBaseUrl_CannotCarryAQueryOrAFragment(string url)
    {
        Validate(Valid() with { BaseUrl = url }).ShouldBe([new FieldError("baseUrl", LlmProviderErrors.QueryOrFragment)]);
    }

    [Fact]
    public void TheBaseUrl_IsLimitedInLength()
    {
        var tooLong = "https://example.com/" + new string('a', LlmProviderLimits.BaseUrl);

        Validate(Valid() with { BaseUrl = tooLong }).ShouldBe([new FieldError("baseUrl", LlmProviderErrors.TooLong)]);
    }

    [Fact]
    public void AnErrorOnTheBaseUrl_NeverRepeatsTheUrl()
    {
        var errors = Validate(Valid() with { BaseUrl = "ftp://user:secret-password@example.com" });

        errors.ShouldAllBe(error => !error.Code.Contains("secret-password", StringComparison.Ordinal) && !error.Field.Contains("secret", StringComparison.Ordinal));
    }

    // ---- Model ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TheModel_IsRequired(string? model)
    {
        Validate(Valid() with { Model = model }).ShouldBe([new FieldError("model", LlmProviderErrors.Required)]);
    }

    [Fact]
    public void TheModel_IsLimitedInLength_AndCannotContainControlCharacters()
    {
        Validate(Valid() with { Model = new string('m', LlmProviderLimits.Model + 1) }).ShouldBe([new FieldError("model", LlmProviderErrors.TooLong)]);
        Validate(Valid() with { Model = "gpt\n5" }).ShouldBe([new FieldError("model", LlmProviderErrors.InvalidCharacters)]);
    }

    // ---- Key ----

    [Fact]
    public void TheKey_IsLimitedInLength_AfterTrimming()
    {
        Validate(Valid() with { ApiKey = "  " + new string('k', LlmProviderLimits.ApiKey) + "\n" }).ShouldBeEmpty();
        Validate(Valid() with { ApiKey = new string('k', LlmProviderLimits.ApiKey + 1) }).ShouldBe([new FieldError("apiKey", LlmProviderErrors.TooLong)]);
    }

    [Theory]
    [InlineData("sk-abc\r\nX-Injected: 1")]
    [InlineData("sk-\u0000abc")]
    public void TheKey_CannotContainControlCharacters_InTheMiddle(string key)
    {
        Validate(Valid() with { ApiKey = key }).ShouldBe([new FieldError("apiKey", LlmProviderErrors.InvalidCharacters)]);
    }

    [Fact]
    public void TheKey_IsNeverPartOfAnError()
    {
        var errors = Validate(Valid() with { ApiKey = "sk-leaky-secret\nbreak" });

        errors.ShouldAllBe(error => !error.Code.Contains("leaky", StringComparison.Ordinal) && !error.Field.Contains("leaky", StringComparison.Ordinal));
    }

    // ---- Several errors ----

    [Fact]
    public void EveryFieldInError_IsReported_InTheOrderOfTheForm()
    {
        var errors = Validate(new LlmProviderDraft(null, "ftp://x", new string('k', 600), null));

        errors.ShouldBe(
        [
            new FieldError("name", LlmProviderErrors.Required),
            new FieldError("baseUrl", LlmProviderErrors.Scheme),
            new FieldError("apiKey", LlmProviderErrors.TooLong),
            new FieldError("model", LlmProviderErrors.Required),
        ]);
    }

    // ---- The draft ----

    [Fact]
    public void Normalized_TrimsEveryField_AndTurnsMissingOnesIntoEmptyText()
    {
        var normalized = new LlmProviderDraft("  OpenAI ", " https://api.openai.com/v1 ", "  sk-abc\n", " gpt-5-nano ").Normalized();

        normalized.ShouldBe(new LlmProviderDraft("OpenAI", "https://api.openai.com/v1", "sk-abc", "gpt-5-nano"));
        new LlmProviderDraft(null, null, null, null).Normalized().ShouldBe(new LlmProviderDraft(string.Empty, string.Empty, string.Empty, string.Empty));
    }

    [Fact]
    public void ToString_NeverContainsTheKeyOrTheBaseUrl()
    {
        var text = new LlmProviderDraft("OpenAI", "https://user:password@api.openai.com/v1", "sk-secret-key", "gpt").ToString();

        text.ShouldNotContain("sk-secret-key");
        text.ShouldNotContain("password");
        text.ShouldContain("OpenAI");
    }
}
