using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Requests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill: give every existing row the same relative order it already displays in
            // (alphabetical by Name within its workspace) so nothing visibly reshuffles the first
            // time this ships — SortOrder only diverges from that once someone drags a task.
            migrationBuilder.Sql(@"
                UPDATE Requests
                SET SortOrder = (
                    SELECT COUNT(*)
                    FROM Requests AS r2
                    WHERE r2.WorkspaceId = Requests.WorkspaceId
                      AND (r2.Name < Requests.Name OR (r2.Name = Requests.Name AND r2.Id < Requests.Id))
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Requests");
        }
    }
}
