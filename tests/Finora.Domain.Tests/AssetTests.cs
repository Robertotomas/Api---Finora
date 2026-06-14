using Finora.Domain.Entities;
using Finora.Domain.Enums;

namespace Finora.Domain.Tests;

// CurrentValue/LastValuationDate = avaliação mais recente (por data), com fallback para o custo de aquisição.
public class AssetTests
{
    private static Asset Make(decimal cost, DateTime acquisitionDate, params (DateTime date, decimal value)[] valuations)
    {
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            Name = "Ativo",
            Category = AssetCategory.RealEstate,
            AcquisitionCost = cost,
            AcquisitionDate = acquisitionDate,
        };
        foreach (var (date, value) in valuations)
            asset.Valuations.Add(new AssetValuation { Id = Guid.NewGuid(), AssetId = asset.Id, Date = date, Value = value });
        return asset;
    }

    [Fact]
    public void CurrentValue_FallsBackToAcquisitionCost_WhenNoValuations()
    {
        var asset = Make(200_000m, new DateTime(2010, 3, 10, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(200_000m, asset.CurrentValue);
        Assert.Equal(new DateTime(2010, 3, 10, 0, 0, 0, DateTimeKind.Utc), asset.LastValuationDate);
    }

    [Fact]
    public void CurrentValue_IsMostRecentValuation()
    {
        var asset = Make(
            200_000m,
            new DateTime(2010, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            (new DateTime(2010, 3, 10, 0, 0, 0, DateTimeKind.Utc), 200_000m),
            (new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc), 250_000m),
            (new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc), 220_000m));

        Assert.Equal(220_000m, asset.CurrentValue);
        Assert.Equal(new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc), asset.LastValuationDate);
    }

    [Fact]
    public void CurrentValue_IndependentOfInsertionOrder()
    {
        var asset = Make(
            100m,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            (new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc), 300m),
            (new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m),
            (new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc), 200m));

        Assert.Equal(300m, asset.CurrentValue);
    }
}
