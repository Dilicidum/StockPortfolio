using Shouldly;

namespace StockPortfolio.Tests;

/// <summary>
/// Rules 2, 4, 5 and 6 — the onion, asserted layer by layer.
/// </summary>
/// <remarks>
/// <para>
/// <c>.Contracts</c> carries no persistence. <c>.Infrastructure</c> never sees HTTP.
/// <c>.Api</c> never sees the database. <c>Shared.Kernel</c> sees neither.
/// </para>
/// <para>
/// The reference checks are transitive over first-party edges, not merely direct: pulling ASP.NET
/// Core into <c>.Infrastructure</c> by way of that module's own <c>.Api</c> is the same
/// violation as referencing the framework outright, and a direct-only test would wave it through.
/// </para>
/// </remarks>
public sealed class LayerReferenceTests
{
    /// <summary>The four <c>.Contracts</c> assemblies.</summary>
    public static TheoryData<string> ContractsAssemblies => AssembliesFor(SolutionAssemblies.ContractsLayer);

    /// <summary>The four <c>.Infrastructure</c> assemblies.</summary>
    public static TheoryData<string> InfrastructureAssemblies => AssembliesFor("Infrastructure");

    /// <summary>The four <c>.Api</c> assemblies.</summary>
    // Worth a second look when editing: passing "Infrastructure" here points rule 5 at the wrong
    // layer and it then enforces nothing, while still reporting green.
    public static TheoryData<string> ApiAssemblies => AssembliesFor("Api");

    /// <summary>
    /// Rule 2. <c>.Contracts</c> is the assembly other modules compile against, so a persistence
    /// reference there drags EF Core across every module boundary in the solution.
    /// </summary>
    /// <param name="assemblyName">The <c>.Contracts</c> assembly under inspection.</param>
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

    /// <summary>
    /// Rule 4. Inbound HTTP is presentation. A persistence assembly that can see
    /// <c>IEndpointRouteBuilder</c> will eventually host an endpoint.
    /// </summary>
    /// <param name="assemblyName">The <c>.Infrastructure</c> assembly under inspection.</param>
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

    /// <summary>
    /// Rule 5. A route that can construct a <c>DbContext</c> stops going through the handler, and
    /// the module's whole application layer becomes optional.
    /// </summary>
    /// <param name="assemblyName">The <c>.Api</c> assembly under inspection.</param>
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

    /// <summary>
    /// Rule 6. <c>Shared.Kernel</c> holds <c>Money</c>, <c>AggregateRoot</c> and the CQRS
    /// interfaces and is referenced by all four <c>.Domain</c> projects, so anything it references
    /// is referenced by the entire solution.
    /// </summary>
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

        // Named separately from the allow-list above so the two failures most worth preventing
        // read as themselves rather than as "an unexpected reference".
        SolutionAssemblies.FindForbiddenReferencePath(Name, IsAspNetCore).ShouldBeNull(
            Name + " must never reach ASP.NET Core; that is what Shared.Api exists for.");

        SolutionAssemblies.FindForbiddenReferencePath(Name, IsPersistence).ShouldBeNull(
            Name
                + " must never reach EF Core. AggregateRoot uses [NotMapped] from "
                + "System.ComponentModel.Annotations, part of the shared framework, precisely to "
                + "avoid the reference.");
    }

    /// <summary>
    /// Presses the button on the smoke detector for rules 2, 4, 5 and 6. All four report a pass by
    /// finding nothing, so a walker that always found nothing — a typo'd prefix, a BFS that never
    /// enqueues — would turn every one of them green and stay green. Each assertion below points
    /// the same machinery at an edge that genuinely exists and must be found.
    /// </summary>
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

        // A three-hop edge: Api -> Application -> Domain -> Shared.Kernel. If the walk
        // were direct-only, Infrastructure could pull ASP.NET Core in through its own Api
        // and rule 4 would wave it through.
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
