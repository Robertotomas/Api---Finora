namespace Finora.Application.DTOs.Asset;

public record AssetValuationDto
{
    public Guid Id { get; init; }
    public DateTime Date { get; init; }
    public decimal Value { get; init; }
}
