using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemapTransactionCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A taxonomia de categorias foi substituída. Como os valores antigos do enum
            // deixaram de ter o mesmo significado, normalizamos os dados existentes pela
            // categoria genérica do respetivo tipo (Type: 0=Receita, 1=Despesa, 2=Transferência):
            //   Receitas      -> OtherIncome (9)
            //   Despesas      -> OtherExpense (98)
            //   Transferências -> Transfer (100)
            migrationBuilder.Sql(@"UPDATE ""Transactions"" SET ""Category"" = 9 WHERE ""Type"" = 0;");
            migrationBuilder.Sql(@"UPDATE ""Transactions"" SET ""Category"" = 98 WHERE ""Type"" = 1;");
            migrationBuilder.Sql(@"UPDATE ""Transactions"" SET ""Category"" = 100 WHERE ""Type"" = 2;");

            migrationBuilder.Sql(@"UPDATE ""RecurringTransactions"" SET ""Category"" = 9 WHERE ""Type"" = 0;");
            migrationBuilder.Sql(@"UPDATE ""RecurringTransactions"" SET ""Category"" = 98 WHERE ""Type"" = 1;");
            migrationBuilder.Sql(@"UPDATE ""RecurringTransactions"" SET ""Category"" = 100 WHERE ""Type"" = 2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remapeamento de dados sem reversão possível (a categorização original perde-se).
        }
    }
}
