using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class HouseholdRepository : IHouseholdRepository
{
    private readonly ApplicationDbContext _context;

    public HouseholdRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Household?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Households
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
    }

    public async Task<Household?> GetByIdWithUsersAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Households
            .AsNoTracking()
            .Include(h => h.Users)
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
    }

    public async Task<Household?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Households
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
    }

    public async Task<Household> CreateAsync(Household household, CancellationToken cancellationToken = default)
    {
        _context.Households.Add(household);
        await _context.SaveChangesAsync(cancellationToken);
        return household;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
