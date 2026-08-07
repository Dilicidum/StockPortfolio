using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Shouldly;

using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Persistence;

namespace StockPortfolio.Tests;

public sealed class EfModelTests
{
    private const string ModelOnly = "Host=localhost;Database=model-only;Username=none;Password=none";

    private static IEntityType Entity<TEntity>()
    {
        using var context = new MarketDataDbContext(
            new DbContextOptionsBuilder<MarketDataDbContext>().UseNpgsql(ModelOnly).Options);

        return context.Model.FindEntityType(typeof(TEntity))!;
    }

    // A constructor parameter that no longer names a property fails the whole model at host startup, not on first query.
    [Fact]
    public void UserProviderKey_BindsItsConstructor_ForMaterialisation()
    {
        var binding = Entity<UserProviderKey>().ConstructorBinding;

        binding.ShouldNotBeNull(
            "EF has no constructor binding for UserProviderKey. With no parameterless constructor to "
                + "fall back on, every query would throw.");

        binding.ParameterBindings
            .SelectMany(parameter => parameter.ConsumedProperties)
            .Select(property => property.Name)
            .ShouldBe(["UserId", "Ciphertext", "LastFour", "SavedAt", "LastRejectedAt"], ignoreOrder: true);
    }

    [Fact]
    public void KeyRingEntry_BindsItsConstructor_ForMaterialisation()
    {
        var binding = Entity<KeyRingEntry>().ConstructorBinding;

        binding.ShouldNotBeNull("EF has no constructor binding for KeyRingEntry.");

        binding.ParameterBindings
            .SelectMany(parameter => parameter.ConsumedProperties)
            .Select(property => property.Name)
            .ShouldBe(["Id", "FriendlyName", "Xml"], ignoreOrder: true);
    }

    [Fact]
    public void UserProviderKey_MapsToMarketDataUserProviderKeys_KeyedOnUserId()
    {
        var entity = Entity<UserProviderKey>();

        entity.GetSchema().ShouldBe("marketdata");
        entity.GetTableName().ShouldBe("user_provider_keys");

        entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ShouldBe(["UserId"]);

        entity.GetProperties()
            .Select(property => property.Name)
            .ShouldBe(["UserId", "Ciphertext", "LastFour", "SavedAt", "LastRejectedAt"], ignoreOrder: true);
    }

    [Fact]
    public void KeyRingEntry_MapsToMarketDataDataProtectionKeys()
    {
        var entity = Entity<KeyRingEntry>();

        entity.GetSchema().ShouldBe("marketdata");
        entity.GetTableName().ShouldBe("data_protection_keys");

        entity.GetProperties()
            .Select(property => property.Name)
            .ShouldBe(["Id", "FriendlyName", "Xml"], ignoreOrder: true);
    }
}
