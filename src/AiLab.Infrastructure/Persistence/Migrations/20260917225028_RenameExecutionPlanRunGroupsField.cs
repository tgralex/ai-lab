using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameExecutionPlanRunGroupsField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ExecutionPlanRuns.Groups is stored as a raw JSON blob (see JsonValueConverter.ForObject)
            // whose ExecutionGroupRun entries used the property name "AiRequestIds" before it was
            // renamed to "NodeIds" (node identity split from AiRequestId). NodeIds is a `required`
            // property, so deserializing an old row without that key throws instead of defaulting —
            // this rewrites the stored key name so historical plan runs keep loading. The underlying
            // values are unaffected: they already equal the backfilled node ids for pre-existing data.
            migrationBuilder.Sql("UPDATE ExecutionPlanRuns SET Groups = REPLACE(Groups, '\"AiRequestIds\":', '\"NodeIds\":');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE ExecutionPlanRuns SET Groups = REPLACE(Groups, '\"NodeIds\":', '\"AiRequestIds\":');");
        }
    }
}
