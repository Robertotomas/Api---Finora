namespace Finora.Application.Interfaces;

/// <summary>
/// Sums recurring income minus recurring expenses per account from the first month with data on that account
/// through the given calendar month (capped at UTC "now"), so future months are never included.
/// </summary>
public interface IRecurringAccountBalanceService
{
    Task<IReadOnlyDictionary<Guid, decimal>> GetCumulativeRecurringNetThroughMonthAsync(
        Guid householdId,
        int throughYear,
        int throughMonth,
        CancellationToken cancellationToken = default);
}
