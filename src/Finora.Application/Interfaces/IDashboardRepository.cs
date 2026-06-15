using Finora.Domain.Enums;

namespace Finora.Application.Interfaces;

public record AccountBalanceAtDate(Guid AccountId, string Name, int Type, string Currency, decimal Balance, string? LogoDomain = null);

public interface IDashboardRepository
{
    Task<decimal> GetTotalBalanceAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesAtEndOfMonthAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<decimal> GetMonthlyIncomeAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<decimal> GetMonthlyExpensesAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetExpensesByCategoryAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetIncomeByCategoryAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    /// <summary>Single query: income + expenses for a month.</summary>
    Task<(decimal Income, decimal Expenses)> GetMonthlyIncomeAndExpensesAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    /// <summary>Single query: income + expenses by category for a month.</summary>
    Task<(IReadOnlyList<(int Category, decimal Amount)> Expenses, IReadOnlyList<(int Category, decimal Amount)> Income)> GetAllCategoriesForMonthAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    /// <summary>Range query: income + expenses + categories for multiple months (YTD).</summary>
    Task<(decimal Income, decimal Expenses, IReadOnlyList<(int Category, decimal Amount)> ExpensesByCategory, IReadOnlyList<(int Category, decimal Amount)> IncomeByCategory)> GetRangeAggregateAsync(Guid householdId, int year, int fromMonth, int toMonth, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Year, int Month, decimal Income, decimal Expenses)>> GetMonthlyTrendAsync(Guid householdId, int monthsBack, CancellationToken cancellationToken = default);
    Task<decimal> GetYearlyIncomeAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<decimal> GetYearlyExpensesAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetYearlyExpensesByCategoryAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetYearlyIncomeByCategoryAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesAtEndOfYearAsync(Guid householdId, int year, CancellationToken cancellationToken = default);
    Task<DateTime?> GetEarliestTransactionDateAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalIncomeAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalExpensesAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalIncomeThroughLastClosedMonthAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalExpensesThroughLastClosedMonthAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetTotalExpensesByCategoryAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(int Category, decimal Amount)>> GetTotalIncomeByCategoryAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountBalanceAtDate>> GetAccountBalancesNowAsync(Guid householdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransactionSnapshot>> GetTransactionsInRangeAsync(Guid householdId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransactionWithAccountSnapshot>> GetTransactionsWithAccountInRangeAsync(Guid householdId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}

public record TransactionSnapshot(DateTime Date, TransactionType Type, decimal Amount);
public record TransactionWithAccountSnapshot(DateTime Date, TransactionType Type, decimal Amount, Guid AccountId, Guid? DestinationAccountId);
