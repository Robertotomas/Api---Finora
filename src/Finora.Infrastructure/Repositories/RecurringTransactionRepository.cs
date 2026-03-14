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
                .Sum(r => r.Amount);

            var expenses = recurring
                .Where(r => (r.StartYear < y || (r.StartYear == y && r.StartMonth <= m))
                    && (r.EndYear == null || r.EndYear > y || (r.EndYear == y && r.EndMonth > m))
                    && r.Type == TransactionType.Expense)
                .Sum(r => r.Amount);

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
            .Select(g => ((int)g.Key, g.Sum(r => r.Amount)))
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
            .Select(g => ((int)g.Key, g.Sum(r => r.Amount)))
            .OrderByDescending(x => x.Item2)
            .ToList();
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
}
