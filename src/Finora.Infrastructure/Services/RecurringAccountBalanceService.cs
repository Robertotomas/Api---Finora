using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class RecurringAccountBalanceService : IRecurringAccountBalanceService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRecurringTransactionRepository _recurringRepository;

    public RecurringAccountBalanceService(
        ITransactionRepository transactionRepository,
        IRecurringTransactionRepository recurringRepository)
    {
        _transactionRepository = transactionRepository;
        _recurringRepository = recurringRepository;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetCumulativeRecurringNetThroughMonthAsync(
        Guid householdId,
        int throughYear,
        int throughMonth,
        CancellationToken cancellationToken = default)
    {
        if (throughMonth is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(throughMonth));

        var now = DateTime.UtcNow;
        if (throughYear * 12 + throughMonth > now.Year * 12 + now.Month)
        {
            throughYear = now.Year;
            throughMonth = now.Month;
        }

        var recurring = (await _recurringRepository.GetByHouseholdAsync(householdId, cancellationToken)).ToList();
        if (recurring.Count == 0)
            return new Dictionary<Guid, decimal>();

        var txMins = await _transactionRepository.GetMinTransactionDateByAccountAsync(householdId, cancellationToken);

        var recMinByAccount = new Dictionary<Guid, (int y, int m)>();
        foreach (var r in recurring)
        {
            var ym = r.StartYear * 12 + r.StartMonth;
            if (!recMinByAccount.TryGetValue(r.AccountId, out var min) || ym < min.y * 12 + min.m)
                recMinByAccount[r.AccountId] = (r.StartYear, r.StartMonth);
        }

        var accountIds = new HashSet<Guid>();
        foreach (var id in txMins.Keys)
            accountIds.Add(id);
        foreach (var id in recMinByAccount.Keys)
            accountIds.Add(id);

        var endYm = throughYear * 12 + throughMonth;
        var result = new Dictionary<Guid, decimal>();

        foreach (var accountId in accountIds)
        {
            int? startYm = null;
            if (txMins.TryGetValue(accountId, out var d))
                startYm = d.Year * 12 + d.Month;
            if (recMinByAccount.TryGetValue(accountId, out var rm))
            {
                var rym = rm.y * 12 + rm.m;
                startYm = startYm.HasValue ? Math.Min(startYm.Value, rym) : rym;
            }

            if (!startYm.HasValue)
                continue;

            if (startYm.Value > endYm)
            {
                result[accountId] = 0m;
                continue;
            }

            var sum = 0m;
            var y = (startYm.Value - 1) / 12;
            var mo = (startYm.Value - 1) % 12 + 1;

            while (true)
            {
                foreach (var r in recurring)
                {
                    if (r.AccountId != accountId)
                        continue;
                    if (!IsActiveInMonth(r, y, mo))
                        continue;
                    sum += r.Type == TransactionType.Income ? r.Amount : -r.Amount;
                }

                if (y == throughYear && mo == throughMonth)
                    break;

                mo++;
                if (mo > 12)
                {
                    mo = 1;
                    y++;
                }
            }

            result[accountId] = sum;
        }

        return result;
    }

    private static bool IsActiveInMonth(RecurringTransaction r, int y, int m)
    {
        var started = r.StartYear < y || (r.StartYear == y && r.StartMonth <= m);
        var notEnded = r.EndYear == null || r.EndYear > y || (r.EndYear == y && r.EndMonth > m);
        return started && notEnded;
    }
}
