using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class MonthlyReportRepository : IMonthlyReportRepository
{
    private readonly ApplicationDbContext _context;

    public MonthlyReportRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> ExistsAsync(Guid householdId, int year, int month, CancellationToken cancellationToken = default)
    {
        return await _context.MonthlyReports
            .AsNoTracking()
            .AnyAsync(r => r.HouseholdId == householdId && r.Year == year && r.Month == month, cancellationToken);
    }

    public async Task<MonthlyReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.MonthlyReports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<MonthlyReport>> ListByHouseholdAsync(
        Guid householdId,
        int? year,
        int? month,
        CancellationToken cancellationToken = default)
    {
        var q = _context.MonthlyReports.AsNoTracking().Where(r => r.HouseholdId == householdId);
        if (year.HasValue)
            q = q.Where(r => r.Year == year.Value);
        if (month.HasValue)
            q = q.Where(r => r.Month == month.Value);
        return await q.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToListAsync(cancellationToken);
    }

    public async Task<MonthlyReport> AddAsync(MonthlyReport report, CancellationToken cancellationToken = default)
    {
        _context.MonthlyReports.Add(report);
        await _context.SaveChangesAsync(cancellationToken);
        return report;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateGeneratedMetadataAsync(
        Guid id,
        DateTime generatedAt,
        long fileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.MonthlyReports.FindAsync(new object[] { id }, cancellationToken);
        if (entity == null)
            return false;
        entity.GeneratedAt = generatedAt;
        entity.FileSizeBytes = fileSizeBytes;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
