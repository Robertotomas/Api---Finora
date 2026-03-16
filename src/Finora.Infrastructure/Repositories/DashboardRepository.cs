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

    public async Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesAtEndOfMonthAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default)
    {
        return await GetAccountBalancesAtDateAsync(householdId, new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1), cancellationToken);
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

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetIncomeByCategoryAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var data = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Income && t.Date >= start && t.Date < end)
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

    public async Task<decimal> GetYearlyIncomeAsync(Guid householdId, int year, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        return await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Income && t.Date >= start && t.Date < end)
            .SumAsync(t => t.Amount, cancellationToken);
    }

    public async Task<decimal> GetYearlyExpensesAsync(Guid householdId, int year, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        return await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Expense && t.Date >= start && t.Date < end)
            .SumAsync(t => t.Amount, cancellationToken);
    }

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetYearlyExpensesByCategoryAsync(Guid householdId, int year, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var data = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Expense && t.Date >= start && t.Date < end)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = (int)g.Key, Amount = g.Sum(t => t.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(cancellationToken);

        return data.Select(x => (x.Category, x.Amount)).ToList();
    }

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetYearlyIncomeByCategoryAsync(Guid householdId, int year, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var data = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Income && t.Date >= start && t.Date < end)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = (int)g.Key, Amount = g.Sum(t => t.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(cancellationToken);

        return data.Select(x => (x.Category, x.Amount)).ToList();
    }

    public async Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesAtEndOfYearAsync(Guid householdId, int year, CancellationToken cancellationToken = default)
    {
        return await GetAccountBalancesAtDateAsync(householdId, new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc), cancellationToken);
    }

    public async Task<decimal> GetTotalIncomeAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Income)
            .SumAsync(t => t.Amount, cancellationToken);
    }

    public async Task<decimal> GetTotalExpensesAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Expense)
            .SumAsync(t => t.Amount, cancellationToken);
    }

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetTotalExpensesByCategoryAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var data = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Expense)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = (int)g.Key, Amount = g.Sum(t => t.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(cancellationToken);

        return data.Select(x => (x.Category, x.Amount)).ToList();
    }

    public async Task<IReadOnlyList<(int Category, decimal Amount)>> GetTotalIncomeByCategoryAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var data = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Type == TransactionType.Income)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = (int)g.Key, Amount = g.Sum(t => t.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(cancellationToken);

        return data.Select(x => (x.Category, x.Amount)).ToList();
    }

    public async Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesNowAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await GetAccountBalancesAtDateAsync(householdId, DateTime.UtcNow.AddDays(1), cancellationToken);
    }

    private async Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesAtDateAsync(Guid householdId, DateTime firstDayAfterPeriod, CancellationToken cancellationToken = default)
    {
        var accounts = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.HouseholdId == householdId)
            .Select(a => new { a.Id, a.Name, a.Type, a.Currency, a.Balance })
            .ToListAsync(cancellationToken);

        var deltaByAccount = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.HouseholdId == householdId && t.Date >= firstDayAfterPeriod)
            .GroupBy(t => t.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Delta = g.Sum(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount)
            })
            .ToListAsync(cancellationToken);

        var deltaDict = deltaByAccount.ToDictionary(x => x.AccountId, x => x.Delta);

        return accounts.Select(a =>
        {
            var delta = deltaDict.GetValueOrDefault(a.Id, 0m);
            var balanceAtEnd = a.Balance - delta;
            return new AccountBalanceAtDate(a.Id, a.Name, (int)a.Type, a.Currency ?? "EUR", balanceAtEnd);
        }).ToList();
    }
}
