namespace Finora.Application.DTOs.Reports;

public record MonthlyReportListItemDto
{
    public Guid Id { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public DateTime GeneratedAt { get; init; }
    public long? FileSizeBytes { get; init; }
}
