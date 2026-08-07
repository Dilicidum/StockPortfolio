using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAlerts : Migration
    {
        private static readonly string[] UserIdTickerColumns = ["user_id", "ticker"];

        private static readonly string[] UserIdFiredAtColumns = ["user_id", "fired_at"];

        private static readonly bool[] AscendingThenDescending = [false, true];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "alerts");

            migrationBuilder.CreateTable(
                name: "alert_settings",
                schema: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticker = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    threshold_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    window_minutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fired_alerts",
                schema: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticker = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    change_percent = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    endpoint_percent = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_simulated = table.Column<bool>(type: "boolean", nullable: false),
                    reference_price_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    reference_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    trigger_price_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    trigger_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fired_alerts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alert_settings_user_id_ticker",
                schema: "alerts",
                table: "alert_settings",
                columns: UserIdTickerColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fired_alerts_user_id_fired_at",
                schema: "alerts",
                table: "fired_alerts",
                columns: UserIdFiredAtColumns,
                descending: AscendingThenDescending);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_settings",
                schema: "alerts");

            migrationBuilder.DropTable(
                name: "fired_alerts",
                schema: "alerts");
        }
    }
}
