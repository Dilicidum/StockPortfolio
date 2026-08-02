using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence;

namespace StockPortfolio.Tests;

/// <summary>
/// Proves EF Core can materialise the entities even though neither has a parameterless
/// constructor.
/// </summary>
/// <remarks>
/// <para>
/// The entities expose exactly one constructor: private, taking every mapped value. The usual
/// worry is that EF needs a parameterless constructor and will fail at the first <c>SELECT</c>. It
/// does not — EF Core has bound constructors by convention since 2.1, matching <b>parameter names
/// to property names</b> case-insensitively, and accessibility is irrelevant, so a private one is
/// fine.
/// </para>
/// <para>
/// The reason to pin it with a test rather than trust it: the binding is by NAME. Rename a
/// constructor parameter without renaming its property — <c>createdAt</c> to <c>created</c>, say —
/// and EF silently stops binding the constructor, then throws at runtime because there is no
/// parameterless fallback. That failure would surface as "no suitable constructor" on the first
/// query, far from the rename that caused it. These tests fail at build-and-test time instead.
/// </para>
/// <para>
/// Building the model does not open a connection, so this stays a unit test.
/// </para>
/// </remarks>
public sealed class EfConstructorBindingTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only;Username=none;Password=none")
            .Options;

        using var context = new IdentityDbContext(options);
        return context.Model;
    }

    [Fact]
    public void User_HasNoParameterlessConstructor()
    {
        typeof(User).GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
            .ShouldNotContain(c => c.GetParameters().Length == 0);
    }

    [Fact]
    public void RefreshToken_HasNoParameterlessConstructor()
    {
        typeof(RefreshToken).GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
            .ShouldNotContain(c => c.GetParameters().Length == 0);
    }

    [Theory]
    [InlineData(typeof(User))]
    [InlineData(typeof(RefreshToken))]
    public void Entity_BindsItsAllArgsConstructor_ForMaterialisation(Type clrType)
    {
        var entityType = BuildModel().FindEntityType(clrType);

        entityType.ShouldNotBeNull($"{clrType.Name} is not mapped at all.");

        var binding = entityType.ConstructorBinding;

        binding.ShouldNotBeNull(
            $"EF has no constructor binding for {clrType.Name}. With no parameterless constructor "
            + "to fall back on, every query would throw. The usual cause is a constructor parameter "
            + "whose name no longer matches its property.");

        binding.ParameterBindings.Count.ShouldBeGreaterThan(
            0,
            $"EF bound a zero-argument constructor for {clrType.Name}, which should not exist.");
    }

    [Fact]
    public void User_BindsEveryMappedPropertyThroughTheConstructor()
    {
        var entityType = BuildModel().FindEntityType(typeof(User))!;

        var bound = entityType.ConstructorBinding!.ParameterBindings
            .SelectMany(p => p.ConsumedProperties)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Every scalar EF persists must arrive through the constructor; anything EF had to set
        // afterwards would mean a property the constructor forgot.
        foreach (var property in entityType.GetProperties())
        {
            bound.ShouldContain(
                property.Name,
                $"{property.Name} is mapped but is not a constructor parameter, so EF sets it after "
                + "construction — which means an instance briefly exists in an invalid state.");
        }
    }
}
