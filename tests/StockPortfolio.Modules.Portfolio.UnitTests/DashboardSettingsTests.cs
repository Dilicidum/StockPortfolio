using Shouldly;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Tests;

public sealed class DashboardSettingsTests
{
    [Fact]
    public void CreateDefault_ForAUser_IsSixtySeconds()
    {
        var settings = DashboardSettings.CreateDefault(Guid.NewGuid());

        settings.RefreshInterval.Seconds.ShouldBe(60);
    }

    [Fact]
    public void ChangeInterval_WithANewValue_Replaces()
    {
        var settings = DashboardSettings.CreateDefault(Guid.NewGuid());

        settings.ChangeInterval(RefreshInterval.Create(120).AsT0);

        settings.RefreshInterval.Seconds.ShouldBe(120);
    }
}
