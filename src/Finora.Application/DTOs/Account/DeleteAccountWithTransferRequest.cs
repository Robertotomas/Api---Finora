using System.ComponentModel.DataAnnotations;

namespace Finora.Application.DTOs.Account;

public record DeleteAccountWithTransferRequest
{
    [Required]
    public Guid TargetAccountId { get; init; }
}
