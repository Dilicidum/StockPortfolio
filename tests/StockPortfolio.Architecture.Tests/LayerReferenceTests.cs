using Shouldly;
using Xunit.Sdk;

namespace StockPortfolio.Tests;

public sealed class LayerReferenceTests
{
    public static TheoryData<string> ContractsAssemblies => AssembliesFor(SolutionAssemblies.ContractsLayer);

    public static TheoryData<string> InfrastructureAssemblies => AssembliesFor("Infrastructure");

    public static TheoryData<string> ApiAssemblies => AssembliesFor("Api");

    [Fact]
    public void MemberData_NamesTheLayerEachRuleClaims()
    {
        // A copy-paste once pointed the .Api rule at the Infrastructure layer and it still reported green.
        ShouldNameEveryModulesLayer(ContractsAssemblies, SolutionAssemblies.ContractsLayer);
        ShouldNameEveryModulesLayer(InfrastructureAssemblies, "Infrastructure");
        ShouldNameEveryModulesLayer(ApiAssemblies, "Api");
    }

    [Theory]
    [MemberData(nameof(ContractsAssemblies))]
    public void ContractsAssembly_ReferencesNoPersistence(string assemblyName)
    {
        var assembly = SolutionAssemblies.Get(assemblyName);

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

    [Theory]
    [MemberData(nameof(InfrastructureAssemblies))]
    public void InfrastructureAssembly_ReferencesNoAspNetCore(string assemblyName)
    {
        var assembly = SolutionAssemblies.Get(assemblyName);

        var path = SolutionAssemblies.FindForbiddenReferencePath(assemblyName, IsAspNetCoreWebStack);

        path.ShouldBeNull(
            assemblyName
                + " reaches the ASP.NET Core web stack:"
                + Environment.NewLine
                + "  - "
                + path
                + Environment.NewLine
                + "Endpoints live in .Api. Infrastructure and Api meet only "
                + "through .Application/Abstractions — remove the FrameworkReference, or the "
                + "project reference that leads to it.");
    }

    [Theory]
    [MemberData(nameof(ApiAssemblies))]
    public void ApiAssembly_ReferencesNeitherPersistenceNorItsOwnInfrastructure(string assemblyName)
    {
        var assembly = SolutionAssemblies.Get(assemblyName);

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

        SolutionAssemblies.FindForbiddenReferencePath(Name, IsAspNetCore).ShouldBeNull(
            Name + " must never reach ASP.NET Core; that is what Shared.Api exists for.");

        SolutionAssemblies.FindForbiddenReferencePath(Name, IsPersistence).ShouldBeNull(
            Name
                + " must never reach EF Core. It holds Money, the CQRS interfaces and InvalidInput; "
                + "anything that needs a value converter or a DbContext belongs in .Infrastructure.");
    }

    [Fact]
    public void ReferenceWalker_FindsEdgesThatDoExist_SoAnEmptyResultMeansSomething()
    {
        var apiLayer = SolutionAssemblies.NameOf("Identity", "Api");
        var infrastructure = SolutionAssemblies.NameOf("Identity", "Infrastructure");

        SolutionAssemblies.FindForbiddenReferencePath(apiLayer, IsAspNetCore).ShouldNotBeNull(
            apiLayer + " does reference ASP.NET Core — endpoints live there. Not finding that "
                + "edge means rule 4 cannot find it either.");

        SolutionAssemblies.FindForbiddenReferencePath(infrastructure, IsPersistence).ShouldNotBeNull(
            infrastructure + " does reference EF Core — the DbContext lives there. Not finding that "
                + "edge means rules 2 and 5 cannot find it either.");

        // A three-hop edge: Api -> Application -> Domain -> Shared.Kernel.
        var transitive = SolutionAssemblies.FindForbiddenReferencePath(
            apiLayer,
            name => string.Equals(name, "StockPortfolio.Shared.Kernel", StringComparison.Ordinal));

        transitive.ShouldNotBeNull("The walk must be transitive, not direct-only.");
        transitive.ShouldStartWith(apiLayer, Case.Sensitive);
        transitive.ShouldEndWith("StockPortfolio.Shared.Kernel", Case.Sensitive);
        transitive.ShouldContain(" -> ", Case.Sensitive);

        IsFrameworkOrOneOf("OneOf").ShouldBeTrue();
        IsFrameworkOrOneOf("System.Collections").ShouldBeTrue();
        IsFrameworkOrOneOf("FluentValidation").ShouldBeFalse();
        IsFrameworkOrOneOf("Microsoft.AspNetCore.Http.Abstractions").ShouldBeFalse();
        IsPersistence("Npgsql").ShouldBeTrue();
        IsPersistence("Microsoft.EntityFrameworkCore.Relational").ShouldBeTrue();
        IsAspNetCore("Microsoft.AspNetCore.Http.Results").ShouldBeTrue();

        IsAspNetCoreWebStack("Microsoft.AspNetCore.Identity.EntityFrameworkCore").ShouldBeFalse(
            "The EF store for Identity is the one name rule 4 forgives.");

        IsAspNetCoreWebStack("Microsoft.AspNetCore.Http.Results").ShouldBeTrue(
            "Forgiving the EF store must not forgive the web stack it was carved out of.");

        IsAspNetCoreWebStack("Microsoft.AspNetCore.Identity").ShouldBeTrue(
            "Only the EntityFrameworkCore store is allowed. Microsoft.AspNetCore.Identity itself carries "
                + "SignInManager and the API endpoints, and belongs to the host.");

        IsAspNetCore("Microsoft.AspNetCore.Identity.EntityFrameworkCore").ShouldBeTrue(
            "Rule 6 stays strict: Shared.Kernel may not reference the EF store either.");
    }

    private static TheoryData<string> AssembliesFor(string layer) => [.. NamesFor(layer)];

    private static IEnumerable<string> NamesFor(string layer) => SolutionAssemblies.ModuleNames
        .Select(module => SolutionAssemblies.NameOf(module, layer))
        .Where(SolutionAssemblies.HasCode);

    private static void ShouldNameEveryModulesLayer(TheoryData<string> data, string layer)
    {
        var suffix = "." + layer;

        var named = data
            .Cast<ITheoryDataRow>()
            .Select(row => (string)row.GetData()[0]!)
            .ToList();

        // Counted the same way the data is built; EmptyShells_AreExactlyThePhasesNotYetBuilt is what stops that becoming a quiet hole.
        named.Count.ShouldBe(
            NamesFor(layer).Count(),
            "The " + layer + " rule must run over one assembly per module that has code in that layer.");

        named.ShouldAllBe(
            name => name.EndsWith(suffix, StringComparison.Ordinal),
            "Every assembly the " + layer + " rule runs over must be a ." + layer + " assembly. "
                + "A member data property pointed at the wrong layer reports green while checking "
                + "nothing the rule claims to check:"
                + Environment.NewLine
                + ModuleBoundaryTests.Describe(named));

        named.ShouldBe(
            NamesFor(layer),
            ignoreOrder: true,
            "The " + layer + " rule must name exactly one ." + layer + " assembly per module that has "
                + "code in that layer.");
    }

    private static bool IsPersistence(string? name) =>
        name is not null
        && (name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Npgsql", StringComparison.Ordinal));

    private static bool IsAspNetCore(string? name) =>
        name is not null && name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);

    private static bool IsAspNetCoreWebStack(string? name) =>
        IsAspNetCore(name) && !IsAspNetCoreDataAccess(name);

    // Carries the AspNetCore prefix but is an EF store with no web dependency, so only the Infrastructure rule forgives it.
    private static bool IsAspNetCoreDataAccess(string? name) =>
        string.Equals(name, "Microsoft.AspNetCore.Identity.EntityFrameworkCore", StringComparison.Ordinal);

    private static bool IsFrameworkOrOneOf(string name) =>
        string.Equals(name, "OneOf", StringComparison.Ordinal)
        || string.Equals(name, "netstandard", StringComparison.Ordinal)
        || string.Equals(name, "mscorlib", StringComparison.Ordinal)
        || string.Equals(name, "System", StringComparison.Ordinal)
        || name.StartsWith("System.", StringComparison.Ordinal);
}
