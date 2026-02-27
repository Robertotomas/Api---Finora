using Finora.Application.Interfaces;
using Finora.Domain.Enums;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly ApplicationDbContext _context;

    public DashboardRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> GetTotalBalanceAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.Accounts
            .AsNoTracking()
            .Where(a => a.HouseholdId == householdId)
            .SumAsync(a => a.Balance, cancellationToken);
    }

    public async Task<decimal> GetMonthlyIncomeAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        return await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Income && t.Date >= start && t.Date < end)
            .SumAsync(t => t.Amount, cancellationToken);
    }

    public async Task<decimal> GetMonthlyExpensesAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        return await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Expense && t.Date >= start && t.Date < end)
            .SumAsync(t => t.Amount, cancellationToken);
    }

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetExpensesByCategoryAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var data = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Expense && t.Date >= start && t.Date < end)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = (int)g.Key, Amount = g.Sum(t => t.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(cancellationToken);

        return data.Select(x => (x.Category, x.Amount)).ToList();
    }

    public async Task<IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)>> GetMonthlyTrendAsync(Guid householdId, int monthsBack, CancellationToken cancellationToken = default)
    {
        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddMonths(-monthsBack);

        var data = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Date >= startDate && t.Date < endDate)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Income = g.Sum(x => x.Type == TransactionType.Income ? x.Amount : 0),
                Expenses = g.Sum(x => x.Type == TransactionType.Expense ? x.Amount : 0)
            })
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ToListAsync(cancellationToken);

        return data.Select(x => (x.Year, x.Month, x.Income, x.Expenses)).ToList();
    }
}
