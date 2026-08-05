using System.Reflection;
using Shouldly;

namespace StockPortfolio.Tests;

/// <summary>Rule 1 — a module reaches another module only through its .Contracts assembly — plus the discovery.</summary>
public sealed class ModuleBoundaryTests
{
    /// <summary>Every first-party assembly the rules run over.</summary>
    public static TheoryData<string> ScannedAssemblies => [.. SolutionAssemblies.ScannedNames];

    /// <summary>The guard against a false green.</summary>
    [Fact]
    public void ExpectedAssemblies_AllLoadByName_SoNoRuleScansAnEmptySet()
    {
        SolutionAssemblies.ExpectedNames.Length.ShouldBe(
            17,
            "Three modules times five layers, plus Shared.Kernel and Shared.Api. "
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

    /// <summary>The second half of the guard.</summary>
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

    /// <summary>The third part of the guard: which assemblies are skipped is a decision, not a drift.</summary>
    [Fact]
    public void EmptyShells_AreExactlyThePhasesNotYetBuilt()
    {
        // Hard-coded on purpose: this is the list of rules currently not enforced, so it must change by hand.
        string[] expected =
        [
            "StockPortfolio.Modules.Identity.Contracts",
            "StockPortfolio.Modules.MarketData.Api",
            "StockPortfolio.Modules.MarketData.Application",
            "StockPortfolio.Modules.MarketData.Contracts",
            "StockPortfolio.Modules.MarketData.Domain",
            "StockPortfolio.Modules.MarketData.Infrastructure",
        ];

        var actual = SolutionAssemblies.ScannedNames
            .Where(name => SolutionAssemblies.IsEmptyShell(SolutionAssemblies.Get(name)))
            .Order(StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(
            expected,
            ignoreOrder: false,
            "The set of empty-shell assemblies has moved. Rule 2 runs over Portfolio.Contracts alone "
                + "and skips the other two, so a rule that skips everywhere reports green while enforcing nothing:"
                + Environment.NewLine
                + Describe(expected.Except(actual, StringComparer.Ordinal)
                    .Select(name => name + " now carries code — delete it from the expected list, and "
                        + "check the rules it just switched on actually pass"))
                + Environment.NewLine
                + Describe(actual.Except(expected, StringComparer.Ordinal)
                    .Select(name => name + " has become an empty shell — every rule over it is now "
                        + "silently skipping, which is a regression, not a pass")));
    }

    /// <summary>Rule 1.</summary>
    [Theory]
    [MemberData(nameof(ScannedAssemblies))]
    public void Assembly_ReferencingAnotherModule_ReachesOnlyItsContracts(string assemblyName)
    {
        // No composition-root exemption: this project references neither host, so neither is ever scanned.
        var assembly = SolutionAssemblies.Get(assemblyName);

        SkipIfEmptyShell(assembly, assemblyName);

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

    /// <summary>Rule 1, second half.</summary>
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

    /// <summary>Presses the button on the smoke detector.</summary>
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

    internal static void SkipIfEmptyShell(Assembly assembly, string assemblyName)
    {
        if (SolutionAssemblies.IsEmptyShell(assembly))
        {
            // Portfolio and MarketData are shells until their phase lands.
            Assert.Skip(
                assemblyName
                    + " declares no StockPortfolio type yet (empty shell project), so its reference "
                    + "metadata is trimmed to System.Runtime and this rule has nothing to check. The "
                    + "assembly loads and is scanned, so the rule goes live by itself with the first "
                    + "type the module gains.");
        }
    }

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
