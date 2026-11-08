using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZainRecharge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RechargeTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, computedColumnSql: "'TXN-' + CAST([Id] + 1000 AS nvarchar(20))", stored: true),
                    MobileNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RechargeTransactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RechargeTransactions_MobileNumber_CreatedAt",
                table: "RechargeTransactions",
                columns: new[] { "MobileNumber", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RechargeTransactions_TransactionId",
                table: "RechargeTransactions",
                column: "TransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RechargeTransactions");
        }
    }
}
