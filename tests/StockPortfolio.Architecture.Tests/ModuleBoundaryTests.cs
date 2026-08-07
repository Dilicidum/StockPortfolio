using System.Reflection;
using Shouldly;

namespace StockPortfolio.Tests;

public sealed class ModuleBoundaryTests
{
    // An assembly declaring no type has its project references trimmed out of metadata, so a rule over it walks nothing and reports green.
    public static TheoryData<string> ScannedAssemblies =>
        [.. SolutionAssemblies.ScannedNames.Where(SolutionAssemblies.HasCode)];

    [Fact]
    public void ExpectedAssemblies_AllLoadByName_SoNoRuleScansAnEmptySet()
    {
        SolutionAssemblies.ExpectedNames.Length.ShouldBe(
            22,
            "Four modules times five layers, plus Shared.Kernel and Shared.Api. "
                + "If the project count changed, this list — and the rules below — must change with it.");

        var missing = SolutionAssemblies.ExpectedNames
            .Where(name => TryLoad(name) is null)
            .ToList();

        missing.ShouldBeEmpty(
            "These assemblies could not be loaded by name, so every architecture rule below would "
                + "have silently scanned nothing for them:"
                + Environment.NewLine
                + Describe(missing));
    }

    [Fact]
    public void PopulatedAssemblies_AreNotEmptyShells_SoTheRulesAreNotAllSkipped()
    {
        string[] populated =
        [
            "StockPortfolio.Shared.Kernel",
            "StockPortfolio.Shared.Api",
            SolutionAssemblies.NameOf("Identity", "Domain"),
            SolutionAssemblies.NameOf("Identity", "Application"),
            SolutionAssemblies.NameOf("Identity", "Infrastructure"),
            SolutionAssemblies.NameOf("Identity", "Api"),

            // Five where Identity has four: Portfolio.Contracts carries IUserHoldsTicker, Identity.Contracts is empty on purpose.
            SolutionAssemblies.NameOf("Portfolio", "Contracts"),
            SolutionAssemblies.NameOf("Portfolio", "Domain"),
            SolutionAssemblies.NameOf("Portfolio", "Application"),
            SolutionAssemblies.NameOf("Portfolio", "Infrastructure"),
            SolutionAssemblies.NameOf("Portfolio", "Api"),

            SolutionAssemblies.NameOf("Alerts", "Contracts"),
            SolutionAssemblies.NameOf("Alerts", "Domain"),
            SolutionAssemblies.NameOf("Alerts", "Application"),
            SolutionAssemblies.NameOf("Alerts", "Infrastructure"),
            SolutionAssemblies.NameOf("Alerts", "Api"),

            SolutionAssemblies.NameOf("MarketData", "Domain"),
            SolutionAssemblies.NameOf("MarketData", "Contracts"),
            SolutionAssemblies.NameOf("MarketData", "Application"),
            SolutionAssemblies.NameOf("MarketData", "Infrastructure"),
            SolutionAssemblies.NameOf("MarketData", "Api"),
        ];

        var shells = populated
            .Where(name => SolutionAssemblies.IsEmptyShell(SolutionAssemblies.Get(name)))
            .ToList();

        shells.ShouldBeEmpty(
            "These assemblies carry Phase 1 code, so every rule must actually run against them. "
                + "Reporting as empty shells means the rules below are skipping, not passing:"
                + Environment.NewLine
                + Describe(shells));
    }

    [Fact]
    public void EmptyShells_AreExactlyThePhasesNotYetBuilt()
    {
        // Hard-coded on purpose — this is the list of rules not enforced — and in ordinal order, because the assertion compares in order.
        string[] expected =
        [
            "StockPortfolio.Modules.Identity.Contracts",
        ];

        var actual = SolutionAssemblies.ScannedNames
            .Where(name => SolutionAssemblies.IsEmptyShell(SolutionAssemblies.Get(name)))
            .Order(StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(
            expected,
            ignoreOrder: false,
            "The set of empty-shell assemblies has moved. No rule below generates a case for a name on "
                + "this list, so the list is the honest count of what is not being checked — and this "
                + "test is the only thing that says so out loud:"
                + Environment.NewLine
                + Describe(expected.Except(actual, StringComparer.Ordinal)
                    .Select(name => name + " now carries code — delete it from the expected list, and "
                        + "check the rules it just switched on actually pass"))
                + Environment.NewLine
                + Describe(actual.Except(expected, StringComparer.Ordinal)
                    .Select(name => name + " has become an empty shell — every rule over it has just "
                        + "stopped running, which is a regression, not a pass")));
    }

    [Theory]
    [MemberData(nameof(ScannedAssemblies))]
    public void Assembly_ReferencingAnotherModule_ReachesOnlyItsContracts(string assemblyName)
    {
        // No composition-root exemption: this project references neither host, so neither is ever scanned.
        var assembly = SolutionAssemblies.Get(assemblyName);

        _ = SolutionAssemblies.TryParseModuleLayer(assemblyName, out var ownModule, out _);

        var violations = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => ReachesPastContracts(ownModule, name))
            .Select(name => assemblyName + " -> " + name)
            .ToList();

        violations.ShouldBeEmpty(
            assemblyName
                + " reaches into another module past its .Contracts assembly:"
                + Environment.NewLine
                + Describe(violations)
                + Environment.NewLine
                + "A module may reference only <OtherModule>.Contracts. Delete the ProjectReference and "
                + "go through Contracts, or move the type you need into Contracts as a record of primitives.");
    }

    [Theory]
    [InlineData("StockPortfolio.Shared.Kernel")]
    [InlineData("StockPortfolio.Shared.Api")]
    public void SharedAssembly_ReferencesNoModule_InAnyLayer(string assemblyName)
    {
        var violations = SolutionAssemblies.Get(assemblyName)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => SolutionAssemblies.TryParseModuleLayer(name, out _, out _))
            .Select(name => assemblyName + " -> " + name)
            .ToList();

        violations.ShouldBeEmpty(
            assemblyName
                + " is referenced by every module, so a reference back to one is a cycle in the "
                + "dependency graph:"
                + Environment.NewLine
                + Describe(violations));
    }

    [Theory]
    [InlineData("StockPortfolio.Modules.Portfolio.Domain", true)]
    [InlineData("StockPortfolio.Modules.Portfolio.Application", true)]
    [InlineData("StockPortfolio.Modules.Portfolio.Infrastructure", true)]
    [InlineData("StockPortfolio.Modules.Portfolio.Api", true)]
    [InlineData("StockPortfolio.Modules.Portfolio.Contracts", false)]
    [InlineData("StockPortfolio.Modules.Identity.Domain", false)]
    [InlineData("StockPortfolio.Modules.Identity.Infrastructure", false)]
    [InlineData("StockPortfolio.Shared.Kernel", false)]
    [InlineData("Microsoft.EntityFrameworkCore", false)]
    public void CrossModuleRule_JudgesAReferenceAsExpected(string referenced, bool isViolation) =>
        ReachesPastContracts("Identity", referenced).ShouldBe(
            isViolation,
            "Rule 1 misjudged a reference from an Identity assembly to " + referenced + ".");

    internal static bool ReachesPastContracts(string? ownModule, string? referenceName) =>
        SolutionAssemblies.TryParseModuleLayer(referenceName, out var module, out var layer)
        && !string.Equals(module, ownModule, StringComparison.Ordinal)
        && !string.Equals(layer, SolutionAssemblies.ContractsLayer, StringComparison.Ordinal);

    internal static string Describe(IEnumerable<string> lines) =>
        string.Join(Environment.NewLine, lines.Select(line => "  - " + line));

    private static Assembly? TryLoad(string name)
    {
        try
        {
            return SolutionAssemblies.Get(name);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}
