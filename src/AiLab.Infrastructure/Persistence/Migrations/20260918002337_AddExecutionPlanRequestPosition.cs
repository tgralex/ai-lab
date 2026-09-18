using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionPlanRequestPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PositionX",
                table: "ExecutionPlanRequests",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PositionY",
                table: "ExecutionPlanRequests",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PositionX",
                table: "ExecutionPlanRequests");

            migrationBuilder.DropColumn(
                name: "PositionY",
                table: "ExecutionPlanRequests");
        }
    }
}
