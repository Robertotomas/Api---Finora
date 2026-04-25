using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class RecurringTransactionRepository : IRecurringTransactionRepository
{
    private readonly ApplicationDbContext _context;

    public RecurringTransactionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<RecurringTransaction?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringTransactions
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringTransaction>> GetByHouseholdAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringTransactions
            .AsNoTracking()
            .Where(r => r.HouseholdId == householdId)
            .OrderBy(r => r.StartYear)
            .ThenBy(r => r.StartMonth)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringTransaction>> GetActiveForMonthAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringTransactions
            .AsNoTracking()
            .Where(r => r.HouseholdId == householdId
                && (r.StartYear < year || (r.StartYear == year && r.StartMonth <= month))
                && (r.EndYear == null || r.EndYear > year || (r.EndYear == year && r.EndMonth > month)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)>> GetAmountsByMonthAsync(
        Guid householdId, int startYear, int startMonth, int count, CancellationToken cancellationToken = default)
    {
        var recurring = await _context.RecurringTransactions
            .AsNoTracking()
            .Where(r => r.HouseholdId == householdId)
            .ToListAsync(cancellationToken);

        var result = new List<(int Year, int Month, decimal Income, decimal Expenses)>();
        var y = startYear;
        var m = startMonth;

        for (var i = 0; i < count; i++)
        {
            var income = recurring
                .Where(r => (r.StartYear < y || (r.StartYear == y && r.StartMonth <= m))
                    && (r.EndYear == null || r.EndYear > y || (r.EndYear == y && r.EndMonth > m))
                    && r.Type == TransactionType.Income)
                .Sum(r => r.Frequency == RecurringFrequency.Annual ? Math.Round(r.Amount / 12m, 2) : r.Amount);

            var expenses = recurring
                .Where(r => (r.StartYear < y || (r.StartYear == y && r.StartMonth <= m))
                    && (r.EndYear == null || r.EndYear > y || (r.EndYear == y && r.EndMonth > m))
                    && r.Type == TransactionType.Expense)
                .Sum(r => r.Frequency == RecurringFrequency.Annual ? Math.Round(r.Amount / 12m, 2) : r.Amount);

            result.Add((y, m, income, expenses));

            m++;
            if (m > 12) { m = 1; y++; }
        }

        return result;
    }

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetRecurringExpensesByCategoryAsync(
        Guid householdId, int year, int month, CancellationToken cancellationToken = default)
    {
        var active = await GetActiveForMonthAsync(householdId, year, month, cancellationToken);
        return active
            .Where(r => r.Type == TransactionType.Expense)
            .GroupBy(r => (int)r.Category)
            .Select(g => ((int)g.Key, g.Sum(r => r.Frequency == RecurringFrequency.Annual ? Math.Round(r.Amount / 12m, 2) : r.Amount)))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetRecurringIncomeByCategoryAsync(
        Guid householdId, int year, int month, CancellationToken cancellationToken = default)
    {
        var active = await GetActiveForMonthAsync(householdId, year, month, cancellationToken);
        return active
            .Where(r => r.Type == TransactionType.Income)
            .GroupBy(r => (int)r.Category)
            .Select(g => ((int)g.Key, g.Sum(r => r.Frequency == RecurringFrequency.Annual ? Math.Round(r.Amount / 12m, 2) : r.Amount)))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    public async Task<(int Year, int Month)?> GetMinimumStartMonthAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var first = await _context.RecurringTransactions
            .AsNoTracking()
            .Where(r => r.HouseholdId == householdId)
            .OrderBy(r => r.StartYear)
            .ThenBy(r => r.StartMonth)
            .Select(r => new { r.StartYear, r.StartMonth })
            .FirstOrDefaultAsync(cancellationToken);
        return first == null ? null : (first.StartYear, first.StartMonth);
    }

    public async Task<(decimal TotalIncome, decimal TotalExpenses, IReadOnlyList<(int Category, decimal Amount)> IncomeByCategory, IReadOnlyList<(int Category, decimal Amount)> ExpensesByCategory)> GetAggregatedForMonthRangeAsync(
        Guid householdId, int startYear, int startMonth, int endYear, int endMonth, CancellationToken cancellationToken = default)
    {
        var recurring = await _context.RecurringTransactions
            .AsNoTracking()
            .Where(r => r.HouseholdId == householdId)
            .ToListAsync(cancellationToken);

        if (recurring.Count == 0)
            return (0m, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(int, decimal)>());

        var startYm = startYear * 12 + startMonth;
        var endYm = endYear * 12 + endMonth;
        if (startYm > endYm)
            return (0m, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(int, decimal)>());

        var incomeDict = new Dictionary<int, decimal>();
        var expenseDict = new Dictionary<int, decimal>();
        var totalIncome = 0m;
        var totalExpenses = 0m;

        var y = startYear;
        var m = startMonth;
        while (true)
        {
            var cur = y * 12 + m;
            if (cur > endYm)
                break;

            foreach (var r in recurring)
            {
                var active = (r.StartYear < y || (r.StartYear == y && r.StartMonth <= m))
                    && (r.EndYear == null || r.EndYear > y || (r.EndYear == y && r.EndMonth > m));
                if (!active)
                    continue;

                var amount = r.Frequency == RecurringFrequency.Annual
                    ? Math.Round(r.Amount / 12m, 2)
                    : r.Amount;

                if (r.Type == TransactionType.Income)
                {
                    totalIncome += amount;
                    var c = (int)r.Category;
                    incomeDict[c] = incomeDict.GetValueOrDefault(c) + amount;
                }
                else
                {
                    totalExpenses += amount;
                    var c = (int)r.Category;
                    expenseDict[c] = expenseDict.GetValueOrDefault(c) + amount;
                }
            }

            m++;
            if (m > 12)
            {
                m = 1;
                y++;
            }
        }

        return (
            totalIncome,
            totalExpenses,
            incomeDict.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList(),
            expenseDict.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList());
    }

    public async Task<RecurringTransaction> CreateAsync(RecurringTransaction entity, CancellationToken cancellationToken = default)
    {
        _context.RecurringTransactions.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<RecurringTransaction> UpdateAsync(RecurringTransaction entity, CancellationToken cancellationToken = default)
    {
        _context.RecurringTransactions.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<int> CountByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringTransactions
            .AsNoTracking()
            .CountAsync(r => r.AccountId == accountId || r.DestinationAccountId == accountId, cancellationToken);
    }

    public async Task ReassignAccountAsync(Guid fromAccountId, Guid toAccountId, CancellationToken cancellationToken = default)
    {
        var recurring = await _context.RecurringTransactions
            .Where(r => r.AccountId == fromAccountId || r.DestinationAccountId == fromAccountId)
            .ToListAsync(cancellationToken);

        foreach (var r in recurring)
        {
            if (r.AccountId == fromAccountId)
                r.AccountId = toAccountId;
            if (r.DestinationAccountId == fromAccountId)
                r.DestinationAccountId = toAccountId;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
