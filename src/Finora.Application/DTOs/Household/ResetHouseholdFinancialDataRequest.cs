namespace Finora.Application.DTOs.Household;

public record ResetHouseholdFinancialDataRequest
{
    /// <summary>Must be exactly <c>RECOMECAR</c> (case-sensitive).</summary>
    public string ConfirmPhrase { get; init; } = string.Empty;
}
