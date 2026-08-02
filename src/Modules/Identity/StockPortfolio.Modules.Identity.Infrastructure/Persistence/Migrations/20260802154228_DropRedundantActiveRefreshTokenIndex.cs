using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropRedundantActiveRefreshTokenIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_active",
                schema: "identity",
                table: "refresh_tokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_active",
                schema: "identity",
                table: "refresh_tokens",
                column: "token_hash",
                filter: "superseded_at IS NULL");
        }
    }
}
