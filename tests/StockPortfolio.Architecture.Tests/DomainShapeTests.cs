using System.Reflection;
using System.Runtime.CompilerServices;
using Shouldly;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Tests;

public sealed class DomainShapeTests
{
    private const string IsExternalInitTypeName = "System.Runtime.CompilerServices.IsExternalInit";

    public static TheoryData<string> DomainAssemblies =>
        [.. SolutionAssemblies.ModuleNames.Select(module => SolutionAssemblies.NameOf(module, "Domain"))];

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void DomainType_ExposesNoPublicSetter(string assemblyName)
    {
        var assembly = SolutionAssemblies.Get(assemblyName);

        var violations = assembly.GetTypes()
            .Where(IsDomainType)
            .SelectMany(DescribeMutableProperties)
            .ToList();

        violations.ShouldBeEmpty(
            assemblyName
                + " exposes public setters on domain types:"
                + Environment.NewLine
                + ModuleBoundaryTests.Describe(violations)
                + Environment.NewLine
                + "Use { get; private set; } and change state through a method that enforces the "
                + "invariant. Validation in the setter itself is dead code — EF Core's default "
                + "PropertyAccessMode.PreferField writes the backing field and never calls it.");
    }

    [Fact]
    public void DomainSetterRule_AcceptsTheDomainShapes_AndRejectsAPublicSetter()
    {
        DescribeMutableProperties(typeof(CompliantShapes)).ShouldBeEmpty(
            "private set, protected set, get-only and init are all legal domain shapes.");

        DescribeMutableProperties(typeof(ViolatingShape))
            .ShouldHaveSingleItem()
            .ShouldContain(nameof(ViolatingShape.Mutable), Case.Sensitive);
    }

    [Fact]
    public void DomainSetterRule_SeesTheRealDomainTypes_SoAnEmptyResultMeansSomething()
    {
        var identityDomain = SolutionAssemblies.Get(SolutionAssemblies.NameOf("Identity", "Domain"));

        var scanned = identityDomain.GetTypes()
            .Where(IsDomainType)
            .Select(type => type.FullName!)
            .ToList();

        scanned.ShouldContain(
            typeof(UserPreferences).FullName!,
            "Rule 3's filter no longer selects UserPreferences, so the rule scans a smaller set than it "
                + "reports. It is the only entity Identity.Domain still owns — User and RefreshToken "
                + "belong to ASP.NET Core Identity now.");

        SolutionAssemblies.IsDomainNamespace(typeof(UserPreferences).Namespace).ShouldBeTrue(
            typeof(UserPreferences).Namespace
                + " is a module's domain namespace; not recognising it empties rule 3.");

        SolutionAssemblies.IsDomainNamespace("StockPortfolio.Modules.Identity.Application").ShouldBeFalse(
            "Application is not Domain. Widening the filter would make rule 3 police types it has no "
                + "invariant to police.");

        SolutionAssemblies.IsDomainNamespace("StockPortfolio.Shared.Kernel").ShouldBeFalse(
            "Shared.Kernel is not a module, so it carries no module's domain namespace.");

        // AppUser inherits public setters from IdentityUser and still passes, because the scan asks for DeclaredOnly.
        scanned.ShouldContain(
            typeof(AppUser).FullName!,
            "AppUser is scanned by rule 3 like any other domain type. It is not excused.");

        DescribeMutableProperties(typeof(AppUser)).ShouldBeEmpty(
            "AppUser declares no properties of its own, so rule 3 has nothing to police. If this ever "
                + "reports something, the type has grown a public setter and should get a private one.");
    }

    private static bool IsDomainType(Type type) =>
        SolutionAssemblies.IsDomainNamespace(type.Namespace)
        && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static IEnumerable<string> DescribeMutableProperties(Type type) =>
        type.GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
            .Where(HasPublicSetter)
            .Select(property => type.FullName + "." + property.Name + " { get; set; }");

    private static bool HasPublicSetter(PropertyInfo property)
    {
        // EqualityContract is compiler-emitted and carries no setter of the author's.
        if (string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal)
            || property.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
        {
            return false;
        }

        var setter = property.GetSetMethod(nonPublic: false);

        return setter is not null && !IsInitOnly(setter);
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(modifier => string.Equals(modifier.FullName, IsExternalInitTypeName, StringComparison.Ordinal));

#pragma warning disable CA1812 // Instantiated by nothing: these exist to be reflected over.
    // Abstract, not sealed, so `protected set` is legal here.
    private abstract class CompliantShapes
    {
        public string PrivateSet { get; private set; } = string.Empty;

        public string GetOnly { get; } = string.Empty;

        public string InitOnly { get; init; } = string.Empty;

        protected string ProtectedSet { get; set; } = string.Empty;
    }

    private sealed class ViolatingShape
    {
        public string Mutable { get; set; } = string.Empty;
    }
#pragma warning restore CA1812
}
