using Finora.Domain.Common;
using Finora.Domain.Enums;

namespace Finora.Domain.Entities;

public class Transaction : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public TransactionType Type { get; set; }
    public TransactionCategory Category { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }

    /// <summary>Origem/destino do movimento: uma entidade (empresa/serviço) ou uma pessoa.</summary>
    public TransactionEntityType EntityType { get; set; } = TransactionEntityType.Entity;
    /// <summary>Nome da entidade ou da pessoa associada ao movimento (opcional).</summary>
    public string? EntityName { get; set; }

    public Guid? DestinationAccountId { get; set; }
    public Account? DestinationAccount { get; set; }

    public ICollection<TransactionSplit> Splits { get; set; } = new List<TransactionSplit>();
}
