using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionPlanNodeIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlanNodeId",
                table: "ExecutionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ExecutionPlanRequests",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "ExecutionPlanRequests",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_PlanNodeId",
                table: "ExecutionRuns",
                column: "PlanNodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExecutionRuns_PlanNodeId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "PlanNodeId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "ExecutionPlanRequests");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "ExecutionPlanRequests",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT")
                .Annotation("Sqlite:Autoincrement", true);
        }
    }
}
