using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Shouldly;

using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Tests;

/// <summary>The model must build, and it must bind the one constructor Holding has.</summary>
public sealed class EfModelTests
{
    private const string ModelOnly = "Host=localhost;Database=model-only;Username=none;Password=none";

    private static IEntityType HoldingEntity()
    {
        using var context = new PortfolioDbContext(
            new DbContextOptionsBuilder<PortfolioDbContext>().UseNpgsql(ModelOnly).Options);

        return context.Model.FindEntityType(typeof(Holding))!;
    }

    // Renaming a constructor parameter without renaming its property leaves no bindable constructor,
    // and with no parameterless fallback the WHOLE model fails to build at startup.
    [Fact]
    public void Holding_BindsEveryScalarProperty_ThroughTheConstructor()
    {
        var bound = typeof(Holding)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .ToList();

        bound.ShouldBe(
            ["id", "userId", "ticker", "quantity", "isVisible", "createdAt", "updatedAt"],
            ignoreOrder: true,
            "EF binds by NAME. AveragePrice is absent on purpose — a complex type cannot be a "
                + "constructor parameter (efcore#31621) — and the factory assigns it afterwards.");
    }

    // The companion to the parameter-name list above: EF must actually have chosen that constructor.
    [Fact]
    public void Holding_BindsThatConstructor_ForMaterialisation()
    {
        var binding = HoldingEntity().ConstructorBinding;

        binding.ShouldNotBeNull(
            "EF has no constructor binding for Holding. With no parameterless constructor to fall "
                + "back on, every query would throw. The usual cause is a constructor parameter "
                + "whose name no longer matches its property.");

        binding.ParameterBindings
            .SelectMany(parameter => parameter.ConsumedProperties)
            .Select(property => property.Name)
            .ShouldBe(
                ["Id", "UserId", "Ticker", "Quantity", "IsVisible", "CreatedAt", "UpdatedAt"],
                ignoreOrder: true);
    }

    [Fact]
    public void Holding_HasNoParameterlessConstructor() =>
        typeof(Holding)
            .GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                Type.EmptyTypes)
            .ShouldBeNull("A half-built Holding must not be representable; Create is the only way in.");

    // The cost of mapping Money member by member: a member added later is silently unmapped.
    [Fact]
    public void AveragePrice_MapsEveryMemberOfMoney()
    {
        var mapped = HoldingEntity()
            .GetComplexProperties()
            .ShouldHaveSingleItem()
            .ComplexType
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        var declared = typeof(Money)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        mapped.ShouldBe(
            declared,
            ignoreOrder: true,
            "HoldingConfiguration maps Money member by member, because Money's get-only properties "
                + "are not mapped by convention. A member added to Money is therefore silently "
                + "unmapped until a .Property() line is added beside it.");
    }

    [Fact]
    public void Holdings_AreKeyedOnUserAndTicker_Uniquely() =>
        HoldingEntity()
            .GetIndexes()
            .ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                index => index.IsUnique.ShouldBeTrue(),
                index => index.Properties.Select(property => property.Name).ShouldBe(["UserId", "Ticker"]));

    // Npgsql does not snake-case by convention, so every one of these is a HasColumnName call.
    [Fact]
    public void Holding_MapsToPortfolioHoldings_WithSnakeCasedColumns()
    {
        var entity = HoldingEntity();

        entity.GetSchema().ShouldBe("portfolio");
        entity.GetTableName().ShouldBe("holdings");

        var storeObject = StoreObjectIdentifier.Table("holdings", "portfolio");

        var columns = entity.GetProperties()
            .Concat(entity.GetComplexProperties().SelectMany(complex => complex.ComplexType.GetProperties()))
            .Select(property => property.GetColumnName(storeObject)!)
            .ToList();

        columns.ShouldBe(
            [
                "id",
                "user_id",
                "ticker",
                "quantity",
                "avg_price_amount",
                "avg_price_currency",
                "is_visible",
                "created_at",
                "updated_at",
            ],
            ignoreOrder: true);
    }

    // Without HasPrecision, Npgsql maps decimal to unconstrained numeric, and a later
    // HasPrecision(18,2) would then silently truncate every stored average.
    [Theory]
    [InlineData("Quantity")]
    [InlineData("AveragePrice.Amount")]
    public void MonetaryColumns_CarryEighteenSixPrecision(string path)
    {
        var entity = HoldingEntity();

        var property = path == "Quantity"
            ? entity.FindProperty("Quantity")!
            : entity.GetComplexProperties().Single().ComplexType.FindProperty("Amount")!;

        property.GetPrecision().ShouldBe(18);
        property.GetScale().ShouldBe(6);
    }

    // A bool with a store default of true is the classic "column is always true" bug: the CLR default
    // false reads as "not set", so EF omits it and the database writes true. EF8 fixed it by making the
    // sentinel equal the store default. Phase 5's Hide() depends on that fix, so pin it now.
    [Fact]
    public void IsVisible_DefaultsToTrueInTheDatabase_WithoutSwallowingAnExplicitFalse()
    {
        var isVisible = HoldingEntity().FindProperty("IsVisible")!;

        isVisible.GetDefaultValue().ShouldBe(true);
        isVisible.Sentinel.ShouldBe(
            true,
            "If the sentinel were false, EF would omit IsVisible=false from the INSERT and the store "
                + "default would write true - so a hidden holding would silently save as visible.");
    }

    [Fact]
    public void AveragePriceCurrency_IsFixedLengthThree()
    {
        var currency = HoldingEntity()
            .GetComplexProperties()
            .Single()
            .ComplexType
            .FindProperty("Currency")!;

        currency.GetMaxLength().ShouldBe(3);
        currency.IsFixedLength().ShouldBe(true);
    }
}
