using Finora.Application.Interfaces;
using Finora.Domain.Entities;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Repositories;

public class AssetRepository : IAssetRepository
{
    private readonly ApplicationDbContext _context;

    public AssetRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    // Tracked (com Include) — usado tanto para leitura como para Update/sincronização das avaliações.
    public async Task<Asset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Assets
            .Include(a => a.Valuations)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Asset>> GetByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        return await _context.Assets
            .AsNoTracking()
            .Include(a => a.Valuations)
            .Where(a => a.HouseholdId == householdId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Asset> CreateAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync(cancellationToken);
        return asset;
    }

    public async Task<Asset> UpdateAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        _context.Assets.Update(asset);
        await _context.SaveChangesAsync(cancellationToken);
        return asset;
    }

    public async Task DeleteAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        _context.Assets.Remove(asset);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddValuationAsync(AssetValuation valuation, CancellationToken cancellationToken = default)
    {
        _context.AssetValuations.Add(valuation);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateValuationAsync(AssetValuation valuation, CancellationToken cancellationToken = default)
    {
        _context.AssetValuations.Update(valuation);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteValuationAsync(AssetValuation valuation, CancellationToken cancellationToken = default)
    {
        _context.AssetValuations.Remove(valuation);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
