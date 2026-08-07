using Shouldly;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Tests;

public sealed class AlertWindowTests
{
    private const int MaxMinutes = 60;

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(MaxMinutes)]
    public void Create_AcceptsAWindowInsideTheCap(int minutes) =>
        AlertWindow.Create(minutes, MaxMinutes).AsT0.Minutes.ShouldBe(minutes);

    [Theory]
    [InlineData(MaxMinutes + 1)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void Create_RejectsAnythingOutsideTheCap(int minutes) =>
        AlertWindow.Create(minutes, MaxMinutes).AsT1.Field.ShouldBe("windowMinutes");

    [Fact]
    public void Create_NamesTheCapInTheMessage_SoTheUserLearnsTheNumber() =>
        AlertWindow.Create(MaxMinutes + 1, MaxMinutes).AsT1.Message.ShouldContain(
            MaxMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Case.Sensitive);

    [Fact]
    public void Duration_IsTheMinutesAsATimeSpan() =>
        AlertWindow.Create(15, MaxMinutes).AsT0.Duration.ShouldBe(TimeSpan.FromMinutes(15));

    [Fact]
    public void Create_JudgesAgainstTheCapItIsGiven_NotAFixedOne()
    {
        AlertWindow.Create(90, maxMinutes: 120).AsT0.Minutes.ShouldBe(90);
        AlertWindow.Create(90, maxMinutes: 60).IsT1.ShouldBeTrue();
    }
}
