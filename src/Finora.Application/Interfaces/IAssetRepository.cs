using Finora.Domain.Entities;

namespace Finora.Application.Interfaces;

public interface IAssetRepository
{
    /// <summary>Inclui as avaliações.</summary>
    Task<Asset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Inclui as avaliações de cada ativo.</summary>
    Task<IReadOnlyList<Asset>> GetByHouseholdIdAsync(Guid householdId, CancellationToken cancellationToken = default);

    Task<Asset> CreateAsync(Asset asset, CancellationToken cancellationToken = default);
    Task<Asset> UpdateAsync(Asset asset, CancellationToken cancellationToken = default);
    Task DeleteAsync(Asset asset, CancellationToken cancellationToken = default);

    Task AddValuationAsync(AssetValuation valuation, CancellationToken cancellationToken = default);
    Task UpdateValuationAsync(AssetValuation valuation, CancellationToken cancellationToken = default);
    Task DeleteValuationAsync(AssetValuation valuation, CancellationToken cancellationToken = default);
}
