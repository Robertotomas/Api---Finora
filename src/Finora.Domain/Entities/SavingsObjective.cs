using Finora.Domain.Common;

namespace Finora.Domain.Entities;

public class SavingsObjective : BaseEntity
{
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public DateOnly? TargetDate { get; set; }
    public int SortOrder { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Quando o objetivo concluído foi liquidado (o utilizador confirmou que
    /// registou a despesa real). Continua a aparecer no histórico, mas deixa
    /// de reservar valor do pool de poupança.
    /// </summary>
    public DateTime? LiquidatedAt { get; set; }
}
