using Finora.Domain.Enums;

namespace Finora.Domain.Tests;

// Pesquisa de categoria tolerante a acentos/maiúsculas (usada pelo SearchService).
public class TransactionCategoryLabelsTests
{
    [Fact]
    public void EveryCategory_HasLabel()
    {
        foreach (TransactionCategory c in Enum.GetValues<TransactionCategory>())
            Assert.True(TransactionCategoryLabels.Labels.ContainsKey(c), $"Falta rótulo para {c}");
    }

    [Theory]
    [InlineData("salário")]   // exato com acento
    [InlineData("salario")]   // sem acento
    [InlineData("SALARIO")]   // maiúsculas
    [InlineData("  salario ")] // espaços à volta
    [InlineData("sal")]       // substring
    public void MatchByQuery_FindsSalary_AccentAndCaseInsensitive(string query)
    {
        Assert.Contains(TransactionCategory.Salary, TransactionCategoryLabels.MatchByQuery(query));
    }

    [Fact]
    public void MatchByQuery_AccentedLabel_FoundWithoutAccent()
    {
        // "Farmácia" deve ser encontrada por "farmacia".
        Assert.Contains(TransactionCategory.Pharmacy, TransactionCategoryLabels.MatchByQuery("farmacia"));
    }

    [Fact]
    public void MatchByQuery_MatchesMultipleCategoriesBySharedSubstring()
    {
        // "Reembolsos de compras" e "Reembolso de impostos" partilham "reembolso".
        var hits = TransactionCategoryLabels.MatchByQuery("reembolso");
        Assert.Contains(TransactionCategory.PurchaseRefunds, hits);
        Assert.Contains(TransactionCategory.TaxRefund, hits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MatchByQuery_EmptyOrWhitespace_ReturnsEmpty(string query)
    {
        Assert.Empty(TransactionCategoryLabels.MatchByQuery(query));
    }

    [Fact]
    public void MatchByQuery_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(TransactionCategoryLabels.MatchByQuery("xyzqwerty"));
    }
}
