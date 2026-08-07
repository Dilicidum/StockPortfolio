using Shouldly;

namespace StockPortfolio.Tests;

public sealed class InfrastructureSurfaceTests
{
    // EF writes every generated migration public and restores the modifier on regeneration, so the rule forgives that base type.
    private const string MigrationBaseTypeName = "Microsoft.EntityFrameworkCore.Migrations.Migration";

    public static TheoryData<string> InfrastructureAssemblies =>
        [.. SolutionAssemblies.ModuleNames.Select(module => SolutionAssemblies.NameOf(module, "Infrastructure"))];

    [Theory]
    [MemberData(nameof(InfrastructureAssemblies))]
    public void InfrastructureAssembly_ExportsOnlyItsModuleSeam(string assemblyName)
    {
        var assembly = SolutionAssemblies.Get(assemblyName);

        _ = SolutionAssemblies.TryParseModuleLayer(assemblyName, out var module, out _);
        var seam = module + "Module";

        var exported = assembly.GetExportedTypes();

        var violations = exported
            .Where(type => !IsGeneratedMigration(type))
            .Where(type => !string.Equals(type.Name, seam, StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        violations.ShouldBeEmpty(
            assemblyName
                + " exposes more than its "
                + seam
                + " seam:"
                + Environment.NewLine
                + ModuleBoundaryTests.Describe(violations)
                + Environment.NewLine
                + "Everything in .Infrastructure is internal except "
                + seam
                + ". A public repository, DbContext, store or background service is a type another "
                + "assembly can bind to, and binding to one is how a module stops being replaceable — "
                + "which is the whole reason "
                + seam
                + " exists. Make the type internal; if the host genuinely needs it, register it behind "
                + "an abstraction in .Application/Abstractions and resolve it through "
                + seam
                + ".");

        exported.Select(type => type.Name).ShouldContain(
            seam,
            assemblyName
                + " no longer exports "
                + seam
                + ", so the host has nothing to compose and the check above passed over an empty set.");
    }

    [Fact]
    public void ExportedTypeRule_SeesARealInfrastructureSurface_SoAnEmptyResultMeansSomething()
    {
        var name = SolutionAssemblies.NameOf("Identity", "Infrastructure");
        var assembly = SolutionAssemblies.Get(name);

        var exported = assembly.GetExportedTypes();

        exported.ShouldNotBeEmpty(
            name + " exports nothing at all. Rule 7 passes by finding nothing, so a reflection call that "
                + "returns an empty set would report green over every module at once.");

        exported.Select(type => type.Name).ShouldContain(
            "IdentityModule",
            "IdentityModule is the seam the host calls. Not finding it means GetExportedTypes is not "
                + "seeing this assembly's public surface, so rule 7 is checking nothing.");

        // If GetExportedTypes ever behaved like GetTypes the rule would pass over everything, quietly.
        assembly.GetTypes().Length.ShouldBeGreaterThan(
            exported.Length,
            name + " exports every type it declares. Infrastructure is internal apart from its seam, so "
                + "the two counts must differ.");

        var dbContext = assembly.GetType(
            "StockPortfolio.Modules.Identity.Infrastructure.Persistence.IdentityDbContext",
            throwOnError: true)!;

        dbContext.IsPublic.ShouldBeFalse(
            "IdentityDbContext is the type rule 7 exists to keep out of every other assembly. If this "
                + "ever reads as public, rule 7 must be the thing that fails — not this test.");

        exported.ShouldNotContain(
            dbContext,
            "GetExportedTypes returned an internal type, so rule 7's whole question is being answered "
                + "wrongly and its silence means nothing.");

        IsGeneratedMigration(assembly.GetType("StockPortfolio.Modules.Identity.Infrastructure.IdentityModule", throwOnError: true)!)
            .ShouldBeFalse("The carve-out must not forgive the seam itself, or rule 7 would forgive anything.");

        exported.Where(IsGeneratedMigration).ShouldNotBeEmpty(
            "No exported type in " + name + " is an EF migration, so rule 7's carve-out now forgives "
                + "nothing and should be deleted along with this assertion. Leaving a carve-out that "
                + "matches nothing is how a later public type gets waved through by a rule nobody re-read.");
    }

    private static bool IsGeneratedMigration(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.FullName, MigrationBaseTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
