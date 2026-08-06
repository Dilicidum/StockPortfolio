using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockPortfolio.Modules.MarketData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMarketData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "marketdata");

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "marketdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    friendly_name = table.Column<string>(type: "text", nullable: false),
                    xml = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_provider_keys",
                schema: "marketdata",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ciphertext = table.Column<string>(type: "text", nullable: false),
                    last_four = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_provider_keys", x => x.user_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "marketdata");

            migrationBuilder.DropTable(
                name: "user_provider_keys",
                schema: "marketdata");
        }
    }
}
