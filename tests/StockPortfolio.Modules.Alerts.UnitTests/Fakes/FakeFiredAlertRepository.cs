using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Tests.Fakes;

internal sealed class FakeFiredAlertRepository(List<string> journal) : IFiredAlertRepository
{
    public const string Saved = "saved";

    private readonly List<FiredAlert> _rows = [];

    public IReadOnlyList<FiredAlert> Rows => _rows;

    public Task AddAsync(FiredAlert alert, CancellationToken ct)
    {
        _rows.Add(alert);
        journal.Add(Saved);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FiredAlertRow>> ListRecentAsync(Guid userId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FiredAlertRow>>(
        [
            .. _rows
                .Where(row => row.UserId == userId)
                .OrderByDescending(row => row.FiredAt)
                .Take(limit)
                .Select(row => new FiredAlertRow(
                    row.Id.Value,
                    row.Ticker.Value,
                    row.Direction,
                    row.ChangePercent,
                    row.EndpointPercent,
                    row.TriggerPrice,
                    row.ReferencePrice,
                    row.FiredAt,
                    row.IsSimulated)),
        ]);
}
