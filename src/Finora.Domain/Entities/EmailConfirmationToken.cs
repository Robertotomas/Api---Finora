using Finora.Domain.Common;

namespace Finora.Domain.Entities;

/// <summary>
/// Token de confirmação de email enviado no registo. Mesmo padrão do
/// <see cref="PasswordResetToken"/>: guarda-se só o hash SHA-256 do token bruto,
/// que vai no link por email. Validade de 24h.
/// </summary>
public class EmailConfirmationToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>SHA-256 hex do token bruto enviado por email.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Depois deste instante o token não pode ser usado.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public bool IsRevoked { get; set; }
}
