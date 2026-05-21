namespace Finora.Application.DTOs.Notification;

public record NotificationDto
{
    public Guid Id { get; init; }
    public int Type { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? RedirectUrl { get; init; }
    public bool IsRead { get; init; }
    public DateTime CreatedAt { get; init; }
}
