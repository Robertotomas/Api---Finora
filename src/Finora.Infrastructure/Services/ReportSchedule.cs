namespace Finora.Infrastructure.Services;

/// <summary>Quais meses devem ter relatório mensal. Lógica pura, testável.</summary>
public static class ReportSchedule
{
    /// <summary>
    /// Meses fechados desde o início do plano pago: do mês de <paramref name="planStart"/> (<b>inclusive</b>)
    /// até ao mês anterior a <paramref name="localNow"/>. O mês em que se subscreve (ou muda de plano, que cria
    /// uma subscrição nova) também tem relatório — cobre o mês inteiro, não só desde o dia da subscrição.
    /// O mês corrente nunca entra (ainda não fechou).
    /// </summary>
    public static IEnumerable<(int Year, int Month)> DueMonths(DateTime planStart, DateTime localNow)
    {
        var y = planStart.Year;
        var m = planStart.Month;
        while (y < localNow.Year || (y == localNow.Year && m < localNow.Month))
        {
            yield return (y, m);
            if (++m > 12) { m = 1; y++; }
        }
    }
}
