using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillExecutionPlanNodeIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every plan created before AddExecutionPlanNodeIdentity has at most one row per
            // AiRequestId (the app itself enforced that), so backfilling the new node Id from
            // AiRequestId is injective and safe. It also means every pre-existing
            // ExecutionPlanDependencies row — which already stores AiRequestId values in its
            // From/ToRequestId columns — keeps working unchanged as a node-id reference, with zero
            // data rewrite needed on that table. Kept as its own migration (rather than appended to
            // AddExecutionPlanNodeIdentity) because SQLite defers that migration's Id-column rebuild;
            // running this UPDATE in the same migration hits it before the rebuild lands.
            migrationBuilder.Sql("UPDATE ExecutionPlanRequests SET Id = AiRequestId;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only migration — nothing to structurally revert. Down migrations for
            // AddExecutionPlanNodeIdentity itself will drop the Id/Label columns this data lived in.
        }
    }
}
