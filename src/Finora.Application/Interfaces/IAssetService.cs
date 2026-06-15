using Finora.Application.DTOs.Asset;

namespace Finora.Application.Interfaces;

public interface IAssetService
{
    Task<IReadOnlyList<AssetDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<AssetDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<AssetDto?> CreateAsync(CreateAssetRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default);
    Task<AssetDto?> UpdateAsync(Guid id, UpdateAssetRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<AssetDto?> AddValuationAsync(Guid assetId, AddValuationRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<AssetDto?> UpdateValuationAsync(Guid assetId, Guid valuationId, AddValuationRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<AssetDto?> DeleteValuationAsync(Guid assetId, Guid valuationId, Guid userId, CancellationToken cancellationToken = default);
}
