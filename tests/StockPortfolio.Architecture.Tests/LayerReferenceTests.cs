using Shouldly;

namespace StockPortfolio.Tests;

/// <summary>Rules 2, 4, 5 and 6 — the onion, asserted layer by layer.</summary>
public sealed class LayerReferenceTests
{
    /// <summary>The four .Contracts assemblies.</summary>
    public static TheoryData<string> ContractsAssemblies => AssembliesFor(SolutionAssemblies.ContractsLayer);

    /// <summary>The four .Infrastructure assemblies.</summary>
    public static TheoryData<string> InfrastructureAssemblies => AssembliesFor("Infrastructure");

    /// <summary>The four .Api assemblies.</summary>
    // Worth a second look when editing: passing "Infrastructure" here points rule 5 at the wrong layer.
    public static TheoryData<string> ApiAssemblies => AssembliesFor("Api");

    /// <summary>Rule 2.</summary>
    [Theory]
    [MemberData(nameof(ContractsAssemblies))]
    public void ContractsAssembly_ReferencesNoPersistence(string assemblyName)
    {
        var assembly = SolutionAssemblies.Get(assemblyName);

        ModuleBoundaryTests.SkipIfEmptyShell(assembly, assemblyName);

        var violations = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(IsPersistence)
            .Select(name => assemblyName + " -> " + name)
            .ToList();

        violations.ShouldBeEmpty(
            assemblyName
                + " is the assembly every other module compiles against, so persistence must not "
                + "appear in it:"
                + Environment.NewLine
                + ModuleBoundaryTests.Describe(violations)
                + Environment.NewLine
                + "Contracts holds records of primitives only — no EF Core, no aggregates, no "
                + "strongly-typed ids.");
    }

    /// <summary>Rule 4.</summary>
    [Theory]
    [MemberData(nameof(InfrastructureAssemblies))]
    public void InfrastructureAssembly_ReferencesNoAspNetCore(string assemblyName)
    {
        var assembly = SolutionAssemblies.Get(assemblyName);

        ModuleBoundaryTests.SkipIfEmptyShell(assembly, assemblyName);

        var path = SolutionAssemblies.FindForbiddenReferencePath(assemblyName, IsAspNetCore);

        path.ShouldBeNull(
            assemblyName
                + " reaches ASP.NET Core:"
                + Environment.NewLine
                + "  - "
                + path
                + Environment.NewLine
                + "Endpoints live in .Api. Infrastructure and Api meet only "
                + "through .Application/Abstractions — remove the FrameworkReference, or the "
                + "project reference that leads to it.");
    }

    /// <summary>Rule 5.</summary>
    [Theory]
    [MemberData(nameof(ApiAssemblies))]
    public void ApiAssembly_ReferencesNeitherPersistenceNorItsOwnInfrastructure(string assemblyName)
    {
        var assembly = SolutionAssemblies.Get(assemblyName);

        ModuleBoundaryTests.SkipIfEmptyShell(assembly, assemblyName);

        _ = SolutionAssemblies.TryParseModuleLayer(assemblyName, out var module, out _);
        var ownInfrastructure = SolutionAssemblies.NameOf(module!, "Infrastructure");

        var persistencePath = SolutionAssemblies.FindForbiddenReferencePath(assemblyName, IsPersistence);

        persistencePath.ShouldBeNull(
            assemblyName
                + " reaches persistence:"
                + Environment.NewLine
                + "  - "
                + persistencePath
                + Environment.NewLine
                + "A route must go through an ICommandHandler/IQueryHandler in .Application, never "
                + "the database directly.");

        var infrastructurePath = SolutionAssemblies.FindForbiddenReferencePath(
            assemblyName,
            name => string.Equals(name, ownInfrastructure, StringComparison.Ordinal));

        infrastructurePath.ShouldBeNull(
            assemblyName
                + " reaches its own Infrastructure:"
                + Environment.NewLine
                + "  - "
                + infrastructurePath
                + Environment.NewLine
                + "The two halves of a module meet only through .Application/Abstractions. "
                + "Infrastructure is internal apart from its "
                + module
                + "Module seam, which only the host composes.");
    }

    /// <summary>Rule 6.</summary>
    [Fact]
    public void SharedKernel_ReferencesNothingButOneOfAndTheFramework()
    {
        const string Name = "StockPortfolio.Shared.Kernel";

        var violations = SolutionAssemblies.Get(Name)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && !IsFrameworkOrOneOf(name))
            .Select(name => Name + " -> " + name)
            .ToList();

        violations.ShouldBeEmpty(
            Name
                + " must stay framework-free — OneOf and the base class library, nothing else:"
                + Environment.NewLine
                + ModuleBoundaryTests.Describe(violations)
                + Environment.NewLine
                + "Anything that needs IEndpointRouteBuilder belongs in Shared.Api; "
                + "anything that needs a DbContext belongs in a module's .Infrastructure.");

        // Named separately from the allow-list above so the two failures most worth preventing read as.
        SolutionAssemblies.FindForbiddenReferencePath(Name, IsAspNetCore).ShouldBeNull(
            Name + " must never reach ASP.NET Core; that is what Shared.Api exists for.");

        SolutionAssemblies.FindForbiddenReferencePath(Name, IsPersistence).ShouldBeNull(
            Name
                + " must never reach EF Core. AggregateRoot uses [NotMapped] from "
                + "System.ComponentModel.Annotations, part of the shared framework, precisely to "
                + "avoid the reference.");
    }

    /// <summary>Presses the button on the smoke detector for rules 2, 4, 5 and 6.</summary>
    [Fact]
    public void ReferenceWalker_FindsEdgesThatDoExist_SoAnEmptyResultMeansSomething()
    {
        var presentation = SolutionAssemblies.NameOf("Identity", "Api");
        var infrastructure = SolutionAssemblies.NameOf("Identity", "Infrastructure");

        // Direct edges, legitimate where they are, forbidden one layer over.
        SolutionAssemblies.FindForbiddenReferencePath(presentation, IsAspNetCore).ShouldNotBeNull(
            presentation + " does reference ASP.NET Core — endpoints live there. Not finding that "
                + "edge means rule 4 cannot find it either.");

        SolutionAssemblies.FindForbiddenReferencePath(infrastructure, IsPersistence).ShouldNotBeNull(
            infrastructure + " does reference EF Core — the DbContext lives there. Not finding that "
                + "edge means rules 2 and 5 cannot find it either.");

        // A three-hop edge: Api -> Application -> Domain -> Shared.Kernel.
        var transitive = SolutionAssemblies.FindForbiddenReferencePath(
            presentation,
            name => string.Equals(name, "StockPortfolio.Shared.Kernel", StringComparison.Ordinal));

        transitive.ShouldNotBeNull("The walk must be transitive, not direct-only.");
        transitive.ShouldStartWith(presentation, Case.Sensitive);
        transitive.ShouldEndWith("StockPortfolio.Shared.Kernel", Case.Sensitive);
        transitive.ShouldContain(" -> ", Case.Sensitive);

        // And the allow-list behind rule 6 must actually exclude things.
        IsFrameworkOrOneOf("OneOf").ShouldBeTrue();
        IsFrameworkOrOneOf("System.Collections").ShouldBeTrue();
        IsFrameworkOrOneOf("FluentValidation").ShouldBeFalse();
        IsFrameworkOrOneOf("Microsoft.AspNetCore.Http.Abstractions").ShouldBeFalse();
        IsPersistence("Npgsql").ShouldBeTrue();
        IsPersistence("Microsoft.EntityFrameworkCore.Relational").ShouldBeTrue();
        IsAspNetCore("Microsoft.AspNetCore.Http.Results").ShouldBeTrue();
    }

    private static TheoryData<string> AssembliesFor(string layer) =>
        [.. SolutionAssemblies.ModuleNames.Select(module => SolutionAssemblies.NameOf(module, layer))];

    private static bool IsPersistence(string? name) =>
        name is not null
        && (name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Npgsql", StringComparison.Ordinal));

    private static bool IsAspNetCore(string? name) =>
        name is not null && name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);

    private static bool IsFrameworkOrOneOf(string name) =>
        string.Equals(name, "OneOf", StringComparison.Ordinal)
        || string.Equals(name, "netstandard", StringComparison.Ordinal)
        || string.Equals(name, "mscorlib", StringComparison.Ordinal)
        || string.Equals(name, "System", StringComparison.Ordinal)
        || name.StartsWith("System.", StringComparison.Ordinal);
}
