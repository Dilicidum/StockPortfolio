using System.Reflection;
using Shouldly;

namespace StockPortfolio.Tests;

/// <summary>
/// Rule 1 — a module reaches another module only through its <c>.Contracts</c> assembly — plus the
/// discovery guard every other rule in this assembly leans on.
/// </summary>
/// <remarks>
/// The compiler used to enforce this by accident, when everything outside <c>.Contracts</c> was
/// <c>internal</c>. It cannot any more: a module is five assemblies, so <c>Domain</c> and
/// <c>Application</c> have to be public for its own <c>Infrastructure</c> to see them. This file is
/// what replaced the compiler. Weakening it silently re-opens the boundary.
/// </remarks>
public sealed class ModuleBoundaryTests
{
    /// <summary>Every first-party assembly the rules run over.</summary>
    public static TheoryData<string> ScannedAssemblies => [.. SolutionAssemblies.ScannedNames];

    /// <summary>
    /// The guard against a false green. Reflection rules that scan an empty set pass for the wrong
    /// reason, so the expected assemblies are pinned by name and must all load.
    /// </summary>
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

    /// <summary>
    /// The second half of the guard. Rules skip an assembly that holds no first-party types yet,
    /// which is honest but toothless — so the assemblies that <em>do</em> hold code are pinned here.
    /// Empty these and the whole suite would degrade to skips and still report green.
    /// </summary>
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

    /// <summary>
    /// Rule 1. Cross-module coupling is legal only through <c>&lt;Other&gt;.Contracts</c>.
    /// </summary>
    /// <param name="assemblyName">The assembly under inspection.</param>
    [Theory]
    [MemberData(nameof(ScannedAssemblies))]
    public void Assembly_ReferencingAnotherModule_ReachesOnlyItsContracts(string assemblyName)
    {
        if (SolutionAssemblies.IsHost(assemblyName))
        {
            // Api and Migrator are the composition roots: they reference every <M>.Infrastructure
            // and <M>.Api on purpose. Exempting them is not a loophole — it is the whole
            // point of having a host.
            Assert.Skip(
                assemblyName
                    + " is a composition root and is exempt by design: it wires every module's "
                    + "Infrastructure and Api together, which is the one place that is allowed to.");
        }

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

    /// <summary>
    /// Rule 1, second half. <c>Shared.Kernel</c> and <c>Shared.Api</c> are referenced *by*
    /// modules and must never reference one back, in any layer — not even <c>.Contracts</c>.
    /// </summary>
    /// <param name="assemblyName">The shared assembly under inspection.</param>
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

    /// <summary>
    /// Presses the button on the smoke detector. Rule 1 reports "no violations" both when the
    /// boundary holds and when the predicate behind it is broken, and those two look identical in a
    /// test report. This pins the predicate's answers on inputs whose verdict is not in doubt.
    /// </summary>
    /// <param name="referenced">The referenced assembly name.</param>
    /// <param name="isViolation">The verdict rule 1 must reach for a member of <c>Alerts</c>.</param>
    [Theory]
    [InlineData("StockPortfolio.Modules.Portfolio.Domain", true)]
    [InlineData("StockPortfolio.Modules.Portfolio.Application", true)]
    [InlineData("StockPortfolio.Modules.Portfolio.Infrastructure", true)]
    [InlineData("StockPortfolio.Modules.Portfolio.Api", true)]
    [InlineData("StockPortfolio.Modules.Portfolio.Contracts", false)]
    [InlineData("StockPortfolio.Modules.Alerts.Domain", false)]
    [InlineData("StockPortfolio.Modules.Alerts.Infrastructure", false)]
    [InlineData("StockPortfolio.Shared.Kernel", false)]
    [InlineData("Microsoft.EntityFrameworkCore", false)]
    public void CrossModuleRule_JudgesAReferenceAsExpected(string referenced, bool isViolation) =>
        ReachesPastContracts("Alerts", referenced).ShouldBe(
            isViolation,
            "Rule 1 misjudged a reference from an Alerts assembly to " + referenced + ".");

    internal static bool ReachesPastContracts(string? ownModule, string? referenceName) =>
        SolutionAssemblies.TryParseModuleLayer(referenceName, out var module, out var layer)
        && !string.Equals(module, ownModule, StringComparison.Ordinal)
        && !string.Equals(layer, SolutionAssemblies.ContractsLayer, StringComparison.Ordinal);

    internal static void SkipIfEmptyShell(Assembly assembly, string assemblyName)
    {
        if (SolutionAssemblies.IsEmptyShell(assembly))
        {
            // Portfolio, MarketData and Alerts are shells until their phase lands. Such an assembly
            // has an AssemblyRef table of System.Runtime alone no matter what its .csproj declares,
            // so the rule would pass on emptiness rather than on compliance. Skipping says so out
            // loud instead of banking a green it has not earned.
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
