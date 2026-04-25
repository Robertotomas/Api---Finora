namespace Finora.Application.DTOs.Account;

public record ArchiveAccountRequest
{
    public Guid? TargetAccountId { get; init; }
}
