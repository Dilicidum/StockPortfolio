using OneOf;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Domain;

public readonly record struct RefreshInterval(int Seconds)
{
    public const int Minimum = 10;

    public const int Maximum = 300;

    public static RefreshInterval Default => new(60);

    public static OneOf<RefreshInterval, InvalidInput> Create(int seconds) =>
        seconds is >= Minimum and <= Maximum
            ? new RefreshInterval(seconds)
            : new InvalidInput(
                "refreshIntervalSeconds",
                $"Refresh interval must be between {Minimum} and {Maximum} seconds.");
}
