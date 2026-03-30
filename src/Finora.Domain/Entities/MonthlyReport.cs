using Finora.Domain.Common;

namespace Finora.Domain.Entities;

public class MonthlyReport : BaseEntity
{
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    /// <summary>Calendar year of the period covered (e.g. March 2025 → Year=2025, Month=3).</summary>
    public int Year { get; set; }

    public int Month { get; set; }

    /// <summary>When the PDF was successfully produced.</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>Path relative to the uploads root (e.g. reports/{householdId}/2025-03.pdf).</summary>
    public string FileRelativePath { get; set; } = string.Empty;

    public long? FileSizeBytes { get; set; }
}
