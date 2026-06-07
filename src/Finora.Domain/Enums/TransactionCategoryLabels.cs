using System.Globalization;
using System.Text;

namespace Finora.Domain.Enums;

/// <summary>
/// Fonte de verdade dos rótulos PT-PT das categorias (usada pela pesquisa para
/// permitir procurar movimentos/recorrentes pela categoria, mesmo sem nome).
///
/// ⚠️ AO MUDAR AS CATEGORIAS: atualiza o enum <see cref="TransactionCategory"/> E este
/// dicionário (mantém em sincronia com TRANSACTION_CATEGORY_LABELS no frontend,
/// App---Finora/src/types/transaction.ts). É o único sítio do backend onde a pesquisa
/// conhece os nomes das categorias.
/// </summary>
public static class TransactionCategoryLabels
{
    public static readonly IReadOnlyDictionary<TransactionCategory, string> Labels =
        new Dictionary<TransactionCategory, string>
        {
            [TransactionCategory.Salary] = "Salário",
            [TransactionCategory.Freelance] = "Freelance",
            [TransactionCategory.Investment] = "Investimento",
            [TransactionCategory.Gift] = "Presente",
            [TransactionCategory.Refund] = "Reembolso",
            [TransactionCategory.Food] = "Alimentação",
            [TransactionCategory.Transport] = "Transportes",
            [TransactionCategory.Housing] = "Habitação",
            [TransactionCategory.Utilities] = "Utilidades",
            [TransactionCategory.Health] = "Saúde",
            [TransactionCategory.Entertainment] = "Entretenimento",
            [TransactionCategory.Shopping] = "Compras",
            [TransactionCategory.Education] = "Educação",
            [TransactionCategory.Transfer] = "Transferência",
            [TransactionCategory.Other] = "Outro",
        };

    /// <summary>
    /// Devolve as categorias cujo rótulo contém o termo pesquisado
    /// (sem distinção de maiúsculas nem de acentos). Vazio se nada bater.
    /// </summary>
    public static IReadOnlyList<TransactionCategory> MatchByQuery(string query)
    {
        var needle = Normalize(query);
        if (needle.Length == 0)
            return [];

        return Labels
            .Where(kvp => Normalize(kvp.Value).Contains(needle))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>Minúsculas + remoção de acentos para comparação tolerante.</summary>
    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
