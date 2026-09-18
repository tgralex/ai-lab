using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestStopSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Empty-list default, not "" — the column round-trips through JsonValueConverter.StringList,
            // which deserializes with System.Text.Json; "" isn't valid JSON and would throw the moment
            // an existing (pre-migration) row was read back.
            migrationBuilder.AddColumn<string>(
                name: "StopSequences",
                table: "Requests",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StopSequences",
                table: "Requests");
        }
    }
}
