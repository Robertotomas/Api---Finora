using System.Globalization;
using Finora.Infrastructure.Services;

namespace Finora.Domain.Tests;

// Formatação dos valores no relatório PDF: pt-PT explícito, independente da cultura da máquina
// (no servidor Linux a cultura por defeito daria "2,250.00"), igual ao Intl 'pt-PT' da app.
public class ReportFormatTests
{
    private const char Nbsp = ' ';

    private static T WithCulture<T>(string culture, Func<T> f)
    {
        var prev = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); return f(); }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void Money_IgnoresMachineCulture()
    {
        var en = WithCulture("en-US", () => MonthlyReportGenerationService.Money(2250m, "EUR"));
        var inv = WithCulture("", () => MonthlyReportGenerationService.Money(2250m, "EUR"));
        Assert.Equal("2250,00 €", en);
        Assert.Equal(en, inv);
    }

    [Fact]
    public void Money_GroupsThousandsFrom10000_LikeTheApp()
    {
        Assert.Equal("9999,99 €", MonthlyReportGenerationService.Money(9999.99m, "EUR"));
        Assert.Equal($"12{Nbsp}030,00 €".Replace(Nbsp, ' '), MonthlyReportGenerationService.Money(12030m, "EUR").Replace(Nbsp, ' '));
    }

    [Fact]
    public void Money_NegativeAndOtherCurrency()
    {
        Assert.Equal("-150,00 €", MonthlyReportGenerationService.Money(-150m, "EUR"));
        Assert.Equal("99,50 USD", MonthlyReportGenerationService.Money(99.5m, "USD"));
    }

    [Fact]
    public void Pct_UsesComma()
    {
        Assert.Equal("55,6%", WithCulture("en-US", () => MonthlyReportGenerationService.Pct(55.6m)));
    }
}
