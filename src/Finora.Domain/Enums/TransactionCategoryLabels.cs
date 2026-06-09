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
            // Rendimentos
            [TransactionCategory.Salary] = "Salário",
            [TransactionCategory.Investments] = "Investimentos",
            [TransactionCategory.PurchaseRefunds] = "Reembolsos de compras",
            [TransactionCategory.TaxRefund] = "Reembolso de impostos",
            [TransactionCategory.BenefitsPensions] = "Subsídios e pensões",
            [TransactionCategory.SelfEmployment] = "Rendimento independente",
            [TransactionCategory.OtherIncome] = "Outros rendimentos",
            // Alimentação
            [TransactionCategory.Groceries] = "Supermercado",
            [TransactionCategory.Restaurants] = "Restaurantes",
            [TransactionCategory.Cafes] = "Cafés",
            // Habitação
            [TransactionCategory.Rent] = "Renda",
            [TransactionCategory.HouseholdBills] = "Contas da casa",
            // Transportes
            [TransactionCategory.Fuel] = "Combustível",
            [TransactionCategory.PublicTransport] = "Transportes públicos",
            [TransactionCategory.Parking] = "Estacionamento",
            [TransactionCategory.CarMaintenance] = "Manutenção auto",
            [TransactionCategory.TaxiRideshare] = "Táxis e TVDE",
            // Saúde
            [TransactionCategory.Pharmacy] = "Farmácia",
            [TransactionCategory.Health] = "Saúde",
            [TransactionCategory.GymSports] = "Ginásio e desporto",
            // Lazer
            [TransactionCategory.PersonalCare] = "Cuidados pessoais",
            [TransactionCategory.Gifts] = "Prendas",
            [TransactionCategory.Leisure] = "Lazer",
            [TransactionCategory.Travel] = "Viagens",
            [TransactionCategory.Donations] = "Donativos",
            [TransactionCategory.Pets] = "Animais de estimação",
            [TransactionCategory.Subscriptions] = "Subscrições",
            // Compras
            [TransactionCategory.Shopping] = "Compras",
            [TransactionCategory.Clothing] = "Roupa e calçado",
            [TransactionCategory.HomeFurniture] = "Casa e mobiliário",
            [TransactionCategory.Electronics] = "Eletrónica e tecnologia",
            [TransactionCategory.CreditCard] = "Cartão de crédito",
            // Educação e família
            [TransactionCategory.Education] = "Educação",
            [TransactionCategory.Childcare] = "Cuidados infantis",
            // Encargos
            [TransactionCategory.Taxes] = "Impostos",
            [TransactionCategory.FeesCommissions] = "Taxas e comissões",
            [TransactionCategory.ProfessionalServices] = "Serviços profissionais",
            [TransactionCategory.Insurance] = "Seguros",
            // Outros / Transferência
            [TransactionCategory.OtherExpense] = "Outras despesas",
            [TransactionCategory.Transfer] = "Transferências",
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
