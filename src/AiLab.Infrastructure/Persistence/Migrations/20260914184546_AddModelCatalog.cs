using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelCatalogProviderStatuses",
                columns: table => new
                {
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                    LastRefreshAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RefreshSource = table.Column<int>(type: "INTEGER", nullable: false),
                    RefreshStatus = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCatalogProviderStatuses", x => x.ProviderId);
                });

            migrationBuilder.CreateTable(
                name: "ProviderModels",
                columns: table => new
                {
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeprecated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Recommended = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContextWindowTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    SupportsReasoning = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportedReasoningLevels = table.Column<string>(type: "TEXT", nullable: false),
                    SupportsStreaming = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsStructuredOutput = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsToolCalling = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsPromptCacheKey = table.Column<bool>(type: "INTEGER", nullable: false),
                    InputModalities = table.Column<string>(type: "TEXT", nullable: false),
                    OutputModalities = table.Column<string>(type: "TEXT", nullable: false),
                    InputPricePerMillion = table.Column<decimal>(type: "TEXT", nullable: true),
                    CachedInputPricePerMillion = table.Column<decimal>(type: "TEXT", nullable: true),
                    OutputPricePerMillion = table.Column<decimal>(type: "TEXT", nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderModels", x => new { x.ProviderId, x.ModelId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelCatalogProviderStatuses");

            migrationBuilder.DropTable(
                name: "ProviderModels");
        }
    }
}
