namespace Finora.Application.Interfaces;

public interface IMonthlyReportGenerationService
{
    /// <summary>Generates PDF for the given household and calendar month if not already present.</summary>
    Task<Guid?> GenerateForHouseholdMonthAsync(
        Guid householdId,
        Guid actingUserId,
        int year,
        int month,
        CancellationToken cancellationToken = default);

    /// <summary>Scheduler entry: generates due reports for Pro/Couple households on day 1 (per user timezone).</summary>
    Task GenerateDueReportsAsync(CancellationToken cancellationToken = default);
}
