using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using Shouldly;

using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.Alerts.Infrastructure.Persistence;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Tests;

/// <summary>The Alerts model must build, and it must bind the one constructor each entity has.</summary>
public sealed class EfModelTests
{
    private const string ModelOnly = "Host=localhost;Database=model-only;Username=none;Password=none";

    private static IEntityType Entity<TEntity>()
    {
        using var context = new AlertsDbContext(
            new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(ModelOnly).Options);

        return context.Model.FindEntityType(typeof(TEntity))!;
    }

    // Index sort order and a property's provider type are not carried by the read-optimised model that
    // DbContext.Model returns; asking for them there throws rather than answering wrongly.
    private static IEntityType DesignTimeEntity<TEntity>()
    {
        using var context = new AlertsDbContext(
            new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(ModelOnly).Options);

        return context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;
    }

    // The whole model fails to build at host startup, not on the first query, when a constructor
    // parameter no longer names a property. Building it here is the cheap way to find that out.
    [Fact]
    public void AlertSetting_BindsItsConstructor_ForMaterialisation()
    {
        var binding = Entity<AlertSetting>().ConstructorBinding;

        binding.ShouldNotBeNull(
            "EF has no constructor binding for AlertSetting. With no parameterless constructor to "
                + "fall back on, every query would throw.");

        binding.ParameterBindings
            .SelectMany(parameter => parameter.ConsumedProperties)
            .Select(property => property.Name)
            .ShouldBe(["Id", "UserId", "Ticker", "Enabled", "Threshold", "Window"], ignoreOrder: true);
    }

    [Fact]
    public void FiredAlert_BindsItsConstructor_WithoutTheTwoMoneyMembers()
    {
        var binding = Entity<FiredAlert>().ConstructorBinding;

        binding.ShouldNotBeNull("EF has no constructor binding for FiredAlert.");

        binding.ParameterBindings
            .SelectMany(parameter => parameter.ConsumedProperties)
            .Select(property => property.Name)
            .ShouldBe(
                ["Id", "UserId", "Ticker", "Direction", "ChangePercent", "EndpointPercent", "FiredAt", "IsSimulated"],
                ignoreOrder: true,
                "TriggerPrice and ReferencePrice are absent on purpose — a complex type cannot be a "
                    + "constructor parameter (efcore#31621) — and Record assigns them afterwards.");
    }

    // The cost of mapping Money member by member: a member added later is silently unmapped.
    [Fact]
    public void BothPrices_MapEveryMemberOfMoney()
    {
        var declared = typeof(Money)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        var complex = Entity<FiredAlert>().GetComplexProperties().ToList();

        complex.Select(property => property.Name)
            .ShouldBe(["TriggerPrice", "ReferencePrice"], ignoreOrder: true);

        foreach (var price in complex)
        {
            price.ComplexType
                .GetProperties()
                .Select(property => property.Name)
                .ShouldBe(
                    declared,
                    ignoreOrder: true,
                    price.Name + " does not map every member of Money. Money's get-only properties are "
                        + "not mapped by convention, so each one is an explicit .Property() line and a "
                        + "member added to Money is silently unmapped until someone adds another.");
        }
    }

    [Fact]
    public void AlertSetting_MapsToAlertsAlertSettings_WithSnakeCasedColumns()
    {
        var entity = Entity<AlertSetting>();

        entity.GetSchema().ShouldBe("alerts");
        entity.GetTableName().ShouldBe("alert_settings");

        ColumnsOf(entity, "alert_settings").ShouldBe(
            ["id", "user_id", "ticker", "enabled", "threshold_percent", "window_minutes"],
            ignoreOrder: true);
    }

    [Fact]
    public void FiredAlert_MapsToAlertsFiredAlerts_WithSnakeCasedColumns()
    {
        var entity = Entity<FiredAlert>();

        entity.GetSchema().ShouldBe("alerts");
        entity.GetTableName().ShouldBe("fired_alerts");

        ColumnsOf(entity, "fired_alerts").ShouldBe(
            [
                "id",
                "user_id",
                "ticker",
                "direction",
                "change_percent",
                "endpoint_percent",
                "trigger_price_amount",
                "trigger_price_currency",
                "reference_price_amount",
                "reference_price_currency",
                "fired_at",
                "is_simulated",
            ],
            ignoreOrder: true);
    }

    /// <summary>A threshold belongs to a position, not to an account, and only the index promises it.</summary>
    [Fact]
    public void AlertSettings_AreKeyedOnUserAndTicker_Uniquely() =>
        Entity<AlertSetting>()
            .GetIndexes()
            .ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                index => index.IsUnique.ShouldBeTrue(),
                index => index.Properties.Select(property => property.Name).ShouldBe(["UserId", "Ticker"]));

    /// <summary>Newest first for one user is the only read the history endpoint makes.</summary>
    [Fact]
    public void FiredAlerts_AreIndexedOnUserAndFiredAtDescending()
    {
        var index = DesignTimeEntity<FiredAlert>().GetIndexes().ShouldHaveSingleItem();

        index.IsUnique.ShouldBeFalse("two alerts can fire for one user at one instant.");
        index.Properties.Select(property => property.Name).ShouldBe(["UserId", "FiredAt"]);
        index.IsDescending.ShouldBe(
            [false, true],
            "An ascending fired_at makes the index useless to ORDER BY fired_at DESC LIMIT n, which "
                + "is the only query this table has.");
    }

    /// <summary>Stored as text, so an int renumbered by an enum edit cannot silently rewrite history.</summary>
    [Fact]
    public void Direction_IsStoredAsItsName()
    {
        var direction = DesignTimeEntity<FiredAlert>().FindProperty("Direction")!;

        direction.GetValueConverter().ShouldNotBeNull().ProviderClrType.ShouldBe(typeof(string));
        direction.GetMaxLength().ShouldBe(8);
    }

    /// <summary>numeric(5,2) is exactly ThresholdPercent's range; unconstrained numeric would let 0.001 in.</summary>
    [Fact]
    public void ThresholdPercent_CarriesFiveTwoPrecision()
    {
        var threshold = Entity<AlertSetting>().FindProperty("Threshold")!;

        threshold.GetPrecision().ShouldBe(5);
        threshold.GetScale().ShouldBe(2);
    }

    [Theory]
    [InlineData("ChangePercent")]
    [InlineData("EndpointPercent")]
    public void RecordedPercentages_CarryEighteenSixPrecision(string name)
    {
        var percent = Entity<FiredAlert>().FindProperty(name)!;

        percent.GetPrecision().ShouldBe(18);
        percent.GetScale().ShouldBe(6);
    }

    // The classic "column is always true" bug: the CLR default false reads as "not set", so EF omits it
    // and the store default writes true. EF8 fixed it by making the sentinel equal the store default.
    [Fact]
    public void Enabled_DefaultsToTrueInTheDatabase_WithoutSwallowingAnExplicitFalse()
    {
        var enabled = Entity<AlertSetting>().FindProperty("Enabled")!;

        enabled.GetDefaultValue().ShouldBe(true);
        enabled.Sentinel.ShouldBe(
            true,
            "If the sentinel were false, EF would omit enabled=false from the INSERT and the store "
                + "default would write true — so a threshold saved switched off would come back on.");
    }

    private static List<string> ColumnsOf(IEntityType entity, string table)
    {
        var storeObject = StoreObjectIdentifier.Table(table, "alerts");

        return
        [
            .. entity.GetProperties()
                .Concat(entity.GetComplexProperties().SelectMany(complex => complex.ComplexType.GetProperties()))
                .Select(property => property.GetColumnName(storeObject)!),
        ];
    }
}
