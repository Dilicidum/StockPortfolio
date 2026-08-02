using System.Reflection;
using System.Runtime.CompilerServices;
using Shouldly;

namespace StockPortfolio.Tests;

/// <summary>
/// Rule 3 — no public settable property under a <c>Modules.*.Domain</c> namespace.
/// </summary>
/// <remarks>
/// <para>
/// The domain is rich: private setters, a private parameterless constructor for EF, a static
/// factory returning a <c>OneOf</c>, and instance methods that enforce invariants. A public setter
/// routes around all of it, and it is the single change that turns an aggregate into a bag of data
/// nobody validates.
/// </para>
/// <para>
/// Two details decide whether this rule works or is noise:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// The setter is fetched with <c>GetSetMethod(nonPublic: false)</c>. The default
/// <c>GetSetMethod()</c> returns private accessors too, so <c>{ get; private set; }</c> — the
/// prescribed shape — would read as a violation and every entity in the solution would fail.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>init</c> is allowed, <c>set</c> is not. An <c>init</c> accessor cannot mutate an existing
/// instance, and every positional record — <c>UserId(Guid Value)</c>, <c>Money(decimal, string)</c>
/// — compiles its parameters to public <c>init</c> properties. Banning it would fail the value
/// objects while catching nothing that a private constructor plus a static factory has not already
/// closed off.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed class DomainShapeTests
{
    private const string IsExternalInitTypeName = "System.Runtime.CompilerServices.IsExternalInit";

    /// <summary>The four <c>.Domain</c> assemblies.</summary>
    public static TheoryData<string> DomainAssemblies =>
        [.. SolutionAssemblies.ModuleNames.Select(module => SolutionAssemblies.NameOf(module, "Domain"))];

    /// <summary>
    /// Rule 3, per module.
    /// </summary>
    /// <param name="assemblyName">The <c>.Domain</c> assembly under inspection.</param>
    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void DomainType_ExposesNoPublicSetter(string assemblyName)
    {
        var assembly = SolutionAssemblies.Get(assemblyName);

        ModuleBoundaryTests.SkipIfEmptyShell(assembly, assemblyName);

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

    /// <summary>
    /// Proves rule 3 discriminates: the shapes the domain actually uses must all read as clean,
    /// and a plain public setter must read as a violation. Without this, a rule that returned
    /// "no violations" for every input would look exactly like a passing rule.
    /// </summary>
    [Fact]
    public void DomainSetterRule_AcceptsTheDomainShapes_AndRejectsAPublicSetter()
    {
        DescribeMutableProperties(typeof(CompliantShapes)).ShouldBeEmpty(
            "private set, protected set, get-only and init are all legal domain shapes.");

        DescribeMutableProperties(typeof(ViolatingShape))
            .ShouldHaveSingleItem()
            .ShouldContain(nameof(ViolatingShape.Mutable), Case.Sensitive);
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
        // A record's EqualityContract is emitted by the compiler and carries no setter anyway;
        // named explicitly so a future compiler change cannot turn it into a spurious failure.
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
    // Abstract, not sealed, so `protected set` is legal here: it is the shape AggregateRoot.Id
    // uses, and the rule has to read it as compliant.
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
