using Finora.Domain.Enums;

namespace Finora.Application.DTOs.Household;

public record HouseholdDto
{
    public Guid Id { get; init; }
    public HouseholdType Type { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CurrentPlan { get; init; } = string.Empty;
    public Guid? PrimaryAccountId { get; init; }

    /// <summary>When set, a partner left this household; the UI may offer reset assistance.</summary>
    public DateTime? PartnerLeftNoticeAtUtc { get; init; }
}
