using LivingPaper.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Shouldly;

namespace LivingPaper.Infrastructure.Tests;

public sealed class DataProtectionSecretProtectorTests : IDisposable
{
    private const string Secret = "sk-proj-This-Is-A-Secret-Key-123";

    private readonly string _keysDirectory = Path.Combine(Path.GetTempPath(), "livingpaper-keys-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_keysDirectory))
        {
            Directory.Delete(_keysDirectory, recursive: true);
        }
    }

    private static DataProtectionSecretProtector NewProtector(IDataProtectionProvider? provider = null) =>
        new(provider ?? new EphemeralDataProtectionProvider());

    [Fact]
    public void Protect_GivesATextThatIsNotTheSecret_AndNeverContainsIt()
    {
        var protectedText = NewProtector().Protect(Secret);

        protectedText.ShouldNotBeNullOrWhiteSpace();
        protectedText.ShouldNotBe(Secret);
        protectedText.ShouldNotContain(Secret);
        protectedText.ShouldNotContain("sk-proj");
    }

    [Fact]
    public void Protect_GivesATextSafeToStoreInJson()
    {
        var protectedText = NewProtector().Protect("a \"quoted\" key with \\ and\nnewline and é 😀");

        protectedText.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '=').ShouldBeTrue();
    }

    [Theory]
    [InlineData("sk-simple")]
    [InlineData("é à ü 日本語 😀")]
    [InlineData("with spaces and \"quotes\"")]
    public void ProtectThenTryUnprotect_GivesTheSecretBack(string secret)
    {
        var protector = NewProtector();

        protector.TryUnprotect(protector.Protect(secret), out var result).ShouldBeTrue();

        result.ShouldBe(secret);
    }

    [Fact]
    public void TryUnprotect_GivesALongKeyBack()
    {
        var protector = NewProtector();
        var key = new string('k', 500);

        protector.TryUnprotect(protector.Protect(key), out var result).ShouldBeTrue();

        result.ShouldBe(key);
    }

    [Fact]
    public void Protect_GivesADifferentTextEachTime_YetBothCanBeRead()
    {
        var protector = NewProtector();

        var first = protector.Protect(Secret);
        var second = protector.Protect(Secret);

        first.ShouldNotBe(second);
        protector.TryUnprotect(first, out var a).ShouldBeTrue();
        protector.TryUnprotect(second, out var b).ShouldBeTrue();
        a.ShouldBe(Secret);
        b.ShouldBe(Secret);
    }

    [Fact]
    public void AnotherProtectorOnTheSameKeys_CanReadWhatWasProtected()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protectedText = NewProtector(provider).Protect(Secret);

        NewProtector(provider).TryUnprotect(protectedText, out var result).ShouldBeTrue();

        result.ShouldBe(Secret);
    }

    [Fact]
    public void TryUnprotect_Fails_WhenTheEncryptionKeysAreNotTheSame_LikeAVolumeRestoredWithoutThem()
    {
        var protectedText = NewProtector(new EphemeralDataProtectionProvider()).Protect(Secret);

        NewProtector(new EphemeralDataProtectionProvider()).TryUnprotect(protectedText, out var result).ShouldBeFalse();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void TryUnprotect_Fails_ForATextProtectedForAnotherPurpose()
    {
        var provider = new EphemeralDataProtectionProvider();
        var foreign = provider.CreateProtector("some other purpose").Protect(Secret);

        NewProtector(provider).TryUnprotect(foreign, out var result).ShouldBeFalse();

        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not protected at all")]
    [InlineData("sk-plain-text-key")]
    [InlineData("%%%")]
    public void TryUnprotect_Fails_ForATextThatWasNeverProtected(string text)
    {
        NewProtector().TryUnprotect(text, out var result).ShouldBeFalse();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void TryUnprotect_Fails_ForATextThatWasAltered()
    {
        var protector = NewProtector();
        var protectedText = protector.Protect(Secret);
        var altered = protectedText[..^4] + (protectedText.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        protector.TryUnprotect(altered, out var result).ShouldBeFalse();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void WithKeysKeptInADirectory_ASecretSurvivesARestart()
    {
        // What the application does: the encryption keys live in data/keys, so a restart finds them again.
        var before = NewProtector(DataProtectionProvider.Create(new DirectoryInfo(_keysDirectory), options => options.SetApplicationName("LivingPaper")));
        var protectedText = before.Protect(Secret);

        var after = NewProtector(DataProtectionProvider.Create(new DirectoryInfo(_keysDirectory), options => options.SetApplicationName("LivingPaper")));

        after.TryUnprotect(protectedText, out var result).ShouldBeTrue();
        result.ShouldBe(Secret);
        Directory.GetFiles(_keysDirectory, "*.xml").ShouldNotBeEmpty();
    }

    [Fact]
    public void Protect_RejectsNull()
    {
        Should.Throw<ArgumentNullException>(() => NewProtector().Protect(null!));
    }
}
