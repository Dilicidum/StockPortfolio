using System.Reflection;
using Shouldly;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Tests;

public sealed class EntityConstructorShapeTests
{
    private const BindingFlags AnyConstructor =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static TheoryData<Type> Entities => [typeof(AlertSetting), typeof(FiredAlert)];

    [Theory]
    [MemberData(nameof(Entities))]
    public void Entity_HasExactlyOneConstructor_AndItTakesArguments(Type entity)
    {
        var constructors = entity.GetConstructors(AnyConstructor);

        constructors.Length.ShouldBe(
            1,
            entity.Name + " must have exactly one constructor. A second one gives EF a choice it "
                + "makes by convention, and the choice cannot be configured.");

        constructors[0].GetParameters().ShouldNotBeEmpty(
            entity.Name + " has a parameterless constructor, which lets a half-built entity exist.");
    }

    [Theory]
    [MemberData(nameof(Entities))]
    public void Entity_NamesEveryConstructorParameterAfterAProperty(Type entity)
    {
        var properties = entity
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphans = entity.GetConstructors(AnyConstructor)[0]
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .Where(name => !properties.Contains(name))
            .ToList();

        orphans.ShouldBeEmpty(
            entity.Name + " has constructor parameters that match no property: "
                + string.Join(", ", orphans)
                + ". EF binds by name, so a rename on one side alone means no constructor can be "
                + "bound — and with no parameterless fallback the whole model fails to build at host "
                + "startup rather than on the first query.");
    }

    // The companion to a rule that passes by finding nothing: this one fails if the search finds nothing.
    [Fact]
    public void ConstructorRule_ReadsRealParameters_SoAnEmptyResultMeansSomething()
    {
        var parameters = typeof(AlertSetting).GetConstructors(AnyConstructor)[0]
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .ToList();

        parameters.ShouldContain("userId");
        parameters.ShouldContain("ticker");
    }

    // Money is a ComplexProperty and cannot be a constructor parameter — efcore#31621.
    [Fact]
    public void FiredAlert_OmitsItsMoneyMembersFromTheConstructor()
    {
        var parameters = typeof(FiredAlert).GetConstructors(AnyConstructor)[0]
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        parameters.ShouldNotContain("triggerPrice");
        parameters.ShouldNotContain("referencePrice");
    }
}
