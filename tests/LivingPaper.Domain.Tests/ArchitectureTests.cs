using System.Reflection;
using Shouldly;

namespace LivingPaper.Domain.Tests;

// Guards the hexagonal architecture: the Domain is the center and depends on nothing else.
// A compiler only records an assembly reference when a type from it is actually used, so these tests
// catch real coupling (a forbidden type used in Domain code), not just a stray <ProjectReference>.
public class ArchitectureTests
{
    private static IEnumerable<string> ReferencedAssemblies() =>
        Assembly.Load("LivingPaper.Domain").GetReferencedAssemblies().Select(name => name.Name!);

    [Fact]
    public void Domain_DoesNotDependOnAnyOtherLivingPaperProject()
    {
        ReferencedAssemblies().Where(name => name.StartsWith("LivingPaper.", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Domain_DoesNotDependOnFrameworksOrInfrastructureLibraries()
    {
        var forbidden = new[] { "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions" };

        ReferencedAssemblies()
            .Where(name => forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ShouldBeEmpty();
    }
}
