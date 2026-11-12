using System.Reflection;

namespace LivingPaper.Web;

// The version of the application: the <Version> of Directory.Build.props, the single place where it is written.
// The SDK adds the commit after a "+" to the informational version: it is not part of the version shown to the reader.
internal static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
