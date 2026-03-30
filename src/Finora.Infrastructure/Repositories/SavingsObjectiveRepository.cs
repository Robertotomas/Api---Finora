using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class SavingsObjectiveRepository : ISavingsObjectiveRepository
{
    private readonly ApplicationDbContext _context;

    public SavingsObjectiveRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SavingsObjective>> GetByHouseholdAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.SavingsObjectives
            .Where(x => x.HouseholdId == householdId)
            .OrderBy(x => x.CompletedAt.HasValue)
            .ThenBy(x => x.SortOrder)
            .ThenByDescending(x => x.CompletedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<SavingsObjective?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.SavingsObjectives.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<int> GetMaxSortOrderAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        var value = await _context.SavingsObjectives
            .Where(x => x.HouseholdId == householdId)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync(cancellationToken);
        return value ?? 0;
    }

    public async Task<SavingsObjective> CreateAsync(SavingsObjective objective, CancellationToken cancellationToken = default)
    {
        _context.SavingsObjectives.Add(objective);
        await _context.SaveChangesAsync(cancellationToken);
        return objective;
    }

    public async Task<SavingsObjective> UpdateAsync(SavingsObjective objective, CancellationToken cancellationToken = default)
    {
        _context.SavingsObjectives.Update(objective);
        await _context.SaveChangesAsync(cancellationToken);
        return objective;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.SavingsObjectives.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
            return false;
        _context.SavingsObjectives.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
