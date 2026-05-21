namespace Finora.Application.DTOs.Notification;

public record MarkBatchReadRequest
{
    public List<Guid> Ids { get; init; } = new();
}
