using Finora.Infrastructure.Services;

namespace Finora.Domain.Tests;

// Meses que devem ter relatório: do mês de início do plano (inclusive) até ao último mês fechado.
public class ReportScheduleTests
{
    private static DateTime D(string s) => DateTime.Parse(s);

    [Fact]
    public void SameMonthAsStart_NoReportYet()
    {
        // Subscreveu a 4 de outubro; ainda é outubro → outubro ainda não fechou.
        Assert.Empty(ReportSchedule.DueMonths(D("2026-10-04"), D("2026-10-20")));
    }

    [Fact]
    public void StartMonthIsIncluded()
    {
        // Subscreveu a 4 de outubro; a 1 de novembro o relatório de outubro já é devido.
        Assert.Equal(new[] { (2026, 10) }, ReportSchedule.DueMonths(D("2026-10-04"), D("2026-11-01")));
    }

    [Fact]
    public void SpansYearBoundary()
    {
        Assert.Equal(
            new[] { (2026, 11), (2026, 12), (2027, 1) },
            ReportSchedule.DueMonths(D("2026-11-30"), D("2027-02-10")));
    }

    [Fact]
    public void CurrentMonthNeverIncluded()
    {
        var months = ReportSchedule.DueMonths(D("2026-01-15"), D("2026-04-01")).ToList();
        Assert.Equal((2026, 3), months[^1]);
        Assert.DoesNotContain((2026, 4), months);
    }
}
