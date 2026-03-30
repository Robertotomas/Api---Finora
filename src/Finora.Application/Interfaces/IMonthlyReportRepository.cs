using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface IMonthlyReportRepository
{
    Task<bool> ExistsAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default);
    Task<MonthlyReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonthlyReport>> ListByHouseholdAsync(Guid householdId, int? year, int? month, CancellationToken cancellationToken = default);
    Task<MonthlyReport> AddAsync(MonthlyReport report, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
