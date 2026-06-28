using Finora.Application.DTOs.Asset;
using Finora.Application.Interfaces;
using Finora.Domain.Entities;

namespace Finora.Infrastructure.Services;

public class AssetService : IAssetService
{
    private readonly IAssetRepository _assetRepository;
    private readonly IUserRepository _userRepository;

    public AssetService(IAssetRepository assetRepository, IUserRepository userRepository)
    {
        _assetRepository = assetRepository;
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<AssetDto>> GetByHouseholdAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return Array.Empty<AssetDto>();

        var assets = await _assetRepository.GetByHouseholdIdAsync(householdId, cancellationToken);
        return assets.Select(ToDto).ToList();
    }

    public async Task<AssetDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await _assetRepository.GetByIdAsync(id, cancellationToken);
        if (asset == null) return null;
        if (!await UserBelongsToHouseholdAsync(userId, asset.HouseholdId, cancellationToken))
            return null;
        return ToDto(asset);
    }

    public async Task<AssetDto?> CreateAsync(CreateAssetRequest request, Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await UserBelongsToHouseholdAsync(userId, householdId, cancellationToken))
            return null;

        var acquisitionDate = ToUtc(request.AcquisitionDate);
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Category = request.Category,
            AcquisitionCost = request.AcquisitionCost,
            Currency = "EUR",
            AcquisitionDate = acquisitionDate,
            HouseholdId = householdId,
            CreatedAt = DateTime.UtcNow
        };

        // Avaliação base = aquisição (mostrada como primeira linha do histórico).
        asset.Valuations.Add(new AssetValuation
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            Date = acquisitionDate,
            Value = request.AcquisitionCost,
            CreatedAt = DateTime.UtcNow
        });

        await _assetRepository.CreateAsync(asset, cancellationToken);
        return ToDto(asset);
    }

    public async Task<AssetDto?> UpdateAsync(Guid id, UpdateAssetRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await _assetRepository.GetByIdAsync(id, cancellationToken);
        if (asset == null) return null;
        if (!await UserBelongsToHouseholdAsync(userId, asset.HouseholdId, cancellationToken))
            return null;

        var newAcquisitionDate = ToUtc(request.AcquisitionDate);

        // Manter a avaliação base (a mais antiga) em sincronia com a aquisição.
        var baseline = asset.Valuations.OrderBy(v => v.Date).ThenBy(v => v.CreatedAt).FirstOrDefault();
        if (baseline != null)
        {
            baseline.Date = newAcquisitionDate;
            baseline.Value = request.AcquisitionCost;
        }

        asset.Name = request.Name.Trim();
        asset.Category = request.Category;
        asset.AcquisitionCost = request.AcquisitionCost;
        asset.AcquisitionDate = newAcquisitionDate;
        asset.UpdatedAt = DateTime.UtcNow;

        await _assetRepository.UpdateAsync(asset, cancellationToken);
        return ToDto(asset);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await _assetRepository.GetByIdAsync(id, cancellationToken);
        if (asset == null) return false;
        if (!await UserBelongsToHouseholdAsync(userId, asset.HouseholdId, cancellationToken))
            return false;

        await _assetRepository.DeleteAsync(asset, cancellationToken);
        return true;
    }

    public async Task<AssetDto?> AddValuationAsync(Guid assetId, AddValuationRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await _assetRepository.GetByIdAsync(assetId, cancellationToken);
        if (asset == null) return null;
        if (!await UserBelongsToHouseholdAsync(userId, asset.HouseholdId, cancellationToken))
            return null;

        var valuation = new AssetValuation
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            Date = ToUtc(request.Date),
            Value = request.Value,
            CreatedAt = DateTime.UtcNow
        };
        // Reflete na coleção já carregada (tracked) → DTO atualizado sem 2.ª query.
        asset.Valuations.Add(valuation);
        await _assetRepository.AddValuationAsync(valuation, cancellationToken);
        return ToDto(asset);
    }

    public async Task<AssetDto?> UpdateValuationAsync(Guid assetId, Guid valuationId, AddValuationRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await _assetRepository.GetByIdAsync(assetId, cancellationToken);
        if (asset == null) return null;
        if (!await UserBelongsToHouseholdAsync(userId, asset.HouseholdId, cancellationToken))
            return null;

        var valuation = asset.Valuations.FirstOrDefault(v => v.Id == valuationId);
        if (valuation == null) return ToDto(asset);

        valuation.Date = ToUtc(request.Date);
        valuation.Value = request.Value;
        valuation.UpdatedAt = DateTime.UtcNow;

        await _assetRepository.UpdateValuationAsync(valuation, cancellationToken);
        // `valuation` já pertence a `asset.Valuations` (mesma instância tracked) → DTO atualizado sem 2.ª query.
        return ToDto(asset);
    }

    public async Task<AssetDto?> DeleteValuationAsync(Guid assetId, Guid valuationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await _assetRepository.GetByIdAsync(assetId, cancellationToken);
        if (asset == null) return null;
        if (!await UserBelongsToHouseholdAsync(userId, asset.HouseholdId, cancellationToken))
            return null;

        var valuation = asset.Valuations.FirstOrDefault(v => v.Id == valuationId);
        if (valuation == null) return ToDto(asset);

        // Tem de sobrar pelo menos uma avaliação (a base/aquisição).
        if (asset.Valuations.Count <= 1)
            throw new InvalidOperationException("Não é possível eliminar a única avaliação do ativo.");

        asset.Valuations.Remove(valuation);
        await _assetRepository.DeleteValuationAsync(valuation, cancellationToken);
        return ToDto(asset);
    }

    private async Task<bool> UserBelongsToHouseholdAsync(Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user != null && user.HouseholdId.HasValue && user.HouseholdId.Value == householdId;
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static AssetDto ToDto(Asset asset)
    {
        return new AssetDto
        {
            Id = asset.Id,
            HouseholdId = asset.HouseholdId,
            Name = asset.Name,
            Category = asset.Category,
            AcquisitionCost = asset.AcquisitionCost,
            Currency = asset.Currency,
            AcquisitionDate = asset.AcquisitionDate,
            CurrentValue = asset.CurrentValue,
            LastValuationDate = asset.LastValuationDate,
            Valuations = asset.Valuations
                .OrderByDescending(v => v.Date)
                .ThenByDescending(v => v.CreatedAt)
                .Select(v => new AssetValuationDto { Id = v.Id, Date = v.Date, Value = v.Value })
                .ToList()
        };
    }
}
