using System.Text;
using LivingPaper.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The tracing of the calls to the language models in Langfuse (PAPER-75): what the environment variables mean, and what the
// application wires (or does not) from them. Nothing here reaches a real Langfuse.
public class LangfuseTests
{
    private const string Public = "pk-lf-test-public";
    private const string Secret = "sk-lf-test-secret-98765";

    private static IConfiguration Config(params (string Name, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(pair => KeyValuePair.Create(pair.Name, pair.Value))).Build();

    private static IConfiguration Complete(params (string Name, string? Value)[] more) =>
        Config([("LANGFUSE_BASE_URL", "http://localhost:3000"), ("LANGFUSE_PUBLIC_KEY", Public), ("LANGFUSE_SECRET_KEY", Secret), .. more]);

    // ---- The variables ----

    [Fact]
    public void Read_ReturnsNothing_WhenNothingIsSet()
    {
        LangfuseSettings.Read(Config()).ShouldBeNull();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("  ", "  ")]
    public void Read_ReturnsNothing_WhenTheKeysAreEmpty_AsInTheExampleFile(string publicKey, string secretKey)
    {
        // .env.example has the base URL set and the keys empty: tracing is then off, with no error.
        LangfuseSettings.Read(Config(("LANGFUSE_BASE_URL", "http://localhost:3000"), ("LANGFUSE_PUBLIC_KEY", publicKey), ("LANGFUSE_SECRET_KEY", secretKey))).ShouldBeNull();
    }

    [Fact]
    public void Read_GivesTheTracesEndpointOfTheBaseUrl_AndTheBasicAuthorization()
    {
        var options = LangfuseSettings.Read(Complete())!;

        options.Endpoint.ToString().ShouldBe("http://localhost:3000/api/public/otel/v1/traces");
        options.AuthorizationHeader.ShouldBe("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Public}:{Secret}")));
        options.CaptureContent.ShouldBeFalse();
    }

    [Theory]
    [InlineData("http://localhost:3000/")]
    [InlineData("  http://localhost:3000  ")]
    public void Read_IgnoresASlashOrSpacesAroundTheBaseUrl(string baseUrl)
    {
        var options = LangfuseSettings.Read(Config(("LANGFUSE_BASE_URL", baseUrl), ("LANGFUSE_PUBLIC_KEY", Public), ("LANGFUSE_SECRET_KEY", Secret)))!;

        options.Endpoint.ToString().ShouldBe("http://localhost:3000/api/public/otel/v1/traces");
    }

    [Fact]
    public void Read_KeepsThePathOfABaseUrlBehindAProxy()
    {
        var options = LangfuseSettings.Read(Config(("LANGFUSE_BASE_URL", "https://example.test/langfuse"), ("LANGFUSE_PUBLIC_KEY", Public), ("LANGFUSE_SECRET_KEY", Secret)))!;

        options.Endpoint.ToString().ShouldBe("https://example.test/langfuse/api/public/otel/v1/traces");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void Read_CapturesTheContentOnlyWhenAskedTo(string value, bool expected)
    {
        LangfuseSettings.Read(Complete(("LANGFUSE_CAPTURE_CONTENT", value)))!.CaptureContent.ShouldBe(expected);
    }

    [Fact]
    public void Read_StopsWithTheNameOfTheVariable_WhenCaptureContentIsNotTrueOrFalse()
    {
        Should.Throw<InvalidOperationException>(() => LangfuseSettings.Read(Complete(("LANGFUSE_CAPTURE_CONTENT", "yes")))).Message.ShouldContain("LANGFUSE_CAPTURE_CONTENT");
    }

    [Theory]
    [InlineData("LANGFUSE_PUBLIC_KEY")]
    [InlineData("LANGFUSE_SECRET_KEY")]
    public void Read_StopsWithTheNameOfTheMissingKey_WhenOnlyOneOfTheTwoIsSet(string missing)
    {
        var values = new List<(string, string?)> { ("LANGFUSE_BASE_URL", "http://localhost:3000"), ("LANGFUSE_PUBLIC_KEY", Public), ("LANGFUSE_SECRET_KEY", Secret) };
        values.RemoveAll(pair => pair.Item1 == missing);

        var exception = Should.Throw<InvalidOperationException>(() => LangfuseSettings.Read(Config([.. values])));

        exception.Message.ShouldContain(missing);
        exception.Message.ShouldNotContain(Secret);
    }

    [Fact]
    public void Read_AsksForTheBaseUrl_WhenKeysAreGivenWithoutOne_SoNothingGoesToTheCloudByDefault()
    {
        var exception = Should.Throw<InvalidOperationException>(() => LangfuseSettings.Read(Config(("LANGFUSE_PUBLIC_KEY", Public), ("LANGFUSE_SECRET_KEY", Secret))));

        exception.Message.ShouldContain("LANGFUSE_BASE_URL");
    }

    [Theory]
    [InlineData("localhost:3000")]
    [InlineData("ftp://example.test")]
    [InlineData("not a url")]
    [InlineData("file:///etc/passwd")]
    public void Read_StopsWithTheNameOfTheVariable_WhenTheBaseUrlIsNotAnHttpAddress(string baseUrl)
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            LangfuseSettings.Read(Config(("LANGFUSE_BASE_URL", baseUrl), ("LANGFUSE_PUBLIC_KEY", Public), ("LANGFUSE_SECRET_KEY", Secret))));

        exception.Message.ShouldContain("LANGFUSE_BASE_URL");
    }

    [Fact]
    public void TheOptions_NeverPrintTheKeys()
    {
        var options = LangfuseSettings.Read(Complete())!;

        $"{options} {options.Endpoint}".ShouldNotContain(Secret);
        options.ToString().ShouldNotContain(Public);
    }

    // ---- The application ----

    [Fact]
    public void WithoutKeys_NothingIsExported_AndTheContentIsNeverCaptured()
    {
        using var factory = new LivingPaperFactory();

        factory.Services.GetService<TracerProvider>().ShouldBeNull();
        factory.Services.GetRequiredService<LlmTracingOptions>().CaptureContent.ShouldBeFalse();
    }

    [Fact]
    public void WithKeys_TheApplicationExportsItsTraces_AndSaysSoAtStart()
    {
        using var factory = LivingPaperFactory.With(settings: new Dictionary<string, string>
        {
            ["LANGFUSE_BASE_URL"] = "http://127.0.0.1:9",
            ["LANGFUSE_PUBLIC_KEY"] = Public,
            ["LANGFUSE_SECRET_KEY"] = Secret,
        });

        factory.Services.GetService<TracerProvider>().ShouldNotBeNull();
        factory.Services.GetRequiredService<LlmTracingOptions>().CaptureContent.ShouldBeFalse();
        factory.Logs.Entries.ShouldContain(entry => entry.Message.Contains("Langfuse", StringComparison.Ordinal) && entry.Message.Contains("not recorded", StringComparison.Ordinal));
        factory.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains(Secret, StringComparison.Ordinal) && !entry.Message.Contains(Public, StringComparison.Ordinal));
    }

    [Fact]
    public void WithCaptureContentOn_TheApplicationSaysTheTextIsSent()
    {
        using var factory = LivingPaperFactory.With(settings: new Dictionary<string, string>
        {
            ["LANGFUSE_BASE_URL"] = "http://127.0.0.1:9",
            ["LANGFUSE_PUBLIC_KEY"] = Public,
            ["LANGFUSE_SECRET_KEY"] = Secret,
            ["LANGFUSE_CAPTURE_CONTENT"] = "true",
        });

        factory.Services.GetRequiredService<LlmTracingOptions>().CaptureContent.ShouldBeTrue();
        factory.Logs.Entries.ShouldContain(entry => entry.Message.Contains("Langfuse", StringComparison.Ordinal) && entry.Message.Contains("are sent", StringComparison.Ordinal));
    }
}
