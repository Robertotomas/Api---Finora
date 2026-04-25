using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;

namespace Finora.Infrastructure.Services;

public class RecurringAccountBalanceService : IRecurringAccountBalanceService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRecurringTransactionRepository _recurringRepository;
    private readonly IAccountRepository _accountRepository;

    public RecurringAccountBalanceService(
        ITransactionRepository transactionRepository,
        IRecurringTransactionRepository recurringRepository,
        IAccountRepository accountRepository)
    {
        _transactionRepository = transactionRepository;
        _recurringRepository = recurringRepository;
        _accountRepository = accountRepository;
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

        // Exclude archived accounts
        var accounts = await _accountRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        var archivedIds = accounts.Where(a => a.IsArchived).Select(a => a.Id).ToHashSet();

        var txMins = await _transactionRepository.GetMinTransactionDateByAccountAsync(householdId, cancellationToken);

        var recMinByAccount = new Dictionary<Guid, (int y, int m)>();
        foreach (var r in recurring)
        {
            var ym = r.StartYear * 12 + r.StartMonth;
            if (!recMinByAccount.TryGetValue(r.AccountId, out var min) || ym < min.y * 12 + min.m)
                recMinByAccount[r.AccountId] = (r.StartYear, r.StartMonth);
            if (r.DestinationAccountId.HasValue)
            {
                if (!recMinByAccount.TryGetValue(r.DestinationAccountId.Value, out var dmin) || ym < dmin.y * 12 + dmin.m)
                    recMinByAccount[r.DestinationAccountId.Value] = (r.StartYear, r.StartMonth);
            }
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
            if (archivedIds.Contains(accountId))
                continue;

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
                    if (!IsActiveInMonth(r, y, mo))
                        continue;

                    var amount = r.Frequency == RecurringFrequency.Annual
                        ? Math.Round(r.Amount / 12m, 2)
                        : r.Amount;

                    if (r.Type == TransactionType.Transfer)
                    {
                        if (r.AccountId == accountId)
                            sum -= amount;
                        if (r.DestinationAccountId == accountId)
                            sum += amount;
                    }
                    else if (r.AccountId == accountId)
                    {
                        sum += r.Type == TransactionType.Income ? amount : -amount;
                    }
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
