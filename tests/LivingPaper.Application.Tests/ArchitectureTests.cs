using System.Reflection;
using Shouldly;

namespace LivingPaper.Application.Tests;

// Guards the hexagonal architecture: Application (use cases and ports) may depend on Domain only.
// It must not know about adapters (Infrastructure, Persistence) or the web layer.
// See the note in the Domain tests: only types that are really used create an assembly reference.
public class ArchitectureTests
{
    private static IEnumerable<string> ReferencedAssemblies() =>
        Assembly.Load("LivingPaper.Application").GetReferencedAssemblies().Select(name => name.Name!);

    [Fact]
    public void Application_DependsOnlyOnDomainAmongLivingPaperProjects()
    {
        ReferencedAssemblies()
            .Where(name => name.StartsWith("LivingPaper.", StringComparison.Ordinal))
            .Where(name => name != "LivingPaper.Domain")
            .ShouldBeEmpty();
    }

    [Fact]
    public void Application_DoesNotDependOnWebOrDataAccessFrameworks()
    {
        var forbidden = new[] { "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore" };

        ReferencedAssemblies()
            .Where(name => forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ShouldBeEmpty();
    }
}
