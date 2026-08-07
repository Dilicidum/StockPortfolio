using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence;

namespace StockPortfolio.Tests;

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

    // UserPreferences is the only entity this module still maps itself; User and RefreshToken are ASP.NET Core Identity's.
    [Fact]
    public void UserPreferences_HasNoParameterlessConstructor()
    {
        typeof(UserPreferences).GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
            .ShouldNotContain(c => c.GetParameters().Length == 0);
    }

    [Theory]
    [InlineData(typeof(UserPreferences))]
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
    public void UserPreferences_BindsEveryMappedPropertyThroughTheConstructor()
    {
        var entityType = BuildModel().FindEntityType(typeof(UserPreferences))!;

        var bound = entityType.ConstructorBinding!.ParameterBindings
            .SelectMany(p => p.ConsumedProperties)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var property in entityType.GetProperties())
        {
            bound.ShouldContain(
                property.Name,
                $"{property.Name} is mapped but is not a constructor parameter, so EF sets it after "
                + "construction — which means an instance briefly exists in an invalid state.");
        }
    }
}
