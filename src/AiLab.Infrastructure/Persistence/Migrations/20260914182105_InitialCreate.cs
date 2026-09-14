using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Filename = table.Column<string>(type: "TEXT", nullable: false),
                    StoredPath = table.Column<string>(type: "TEXT", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionPlans_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    SystemPrompt = table.Column<string>(type: "TEXT", nullable: true),
                    CachedContext_Text = table.Column<string>(type: "TEXT", nullable: false),
                    CachedContext_AttachmentIds = table.Column<string>(type: "TEXT", nullable: false),
                    UserContext_Text = table.Column<string>(type: "TEXT", nullable: false),
                    UserContext_AttachmentIds = table.Column<string>(type: "TEXT", nullable: false),
                    InputBindings = table.Column<string>(type: "TEXT", nullable: false),
                    StructuredOutputSchema = table.Column<string>(type: "TEXT", nullable: true),
                    StreamingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    Reasoning_Effort = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderSettings = table.Column<string>(type: "TEXT", nullable: false),
                    PromptCacheKey = table.Column<string>(type: "TEXT", nullable: true),
                    FailurePolicy = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryPolicy_MaxRetries = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryPolicy_BaseBackoffMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Requests_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceVariables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceVariables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceVariables_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionPlanDependencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionPlanId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionPlanDependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionPlanDependencies_ExecutionPlans_ExecutionPlanId",
                        column: x => x.ExecutionPlanId,
                        principalTable: "ExecutionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionPlanRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AiRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsFinalOutput = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExecutionPlanId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionPlanRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionPlanRequests_ExecutionPlans_ExecutionPlanId",
                        column: x => x.ExecutionPlanId,
                        principalTable: "ExecutionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionPlanRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CumulativeRequestDuration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    TotalInputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalCachedInputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalReasoningTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalEstimatedCost = table.Column<decimal>(type: "TEXT", nullable: false),
                    PlanGraphSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    Groups = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutionRunIds = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionPlanRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionPlanRuns_ExecutionPlans_ExecutionPlanId",
                        column: x => x.ExecutionPlanId,
                        principalTable: "ExecutionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AiRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedModel = table.Column<string>(type: "TEXT", nullable: true),
                    ActualModel = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FirstResponseEventAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FirstOutputTokenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Usage_InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Usage_CachedInputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Usage_OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Usage_ReasoningTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Usage_TotalTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedInputCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    EstimatedCachedInputCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    EstimatedOutputCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    EstimatedTotalCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    Output = table.Column<string>(type: "TEXT", nullable: true),
                    RawProviderResponseJson = table.Column<string>(type: "TEXT", nullable: true),
                    NormalizedProviderRequestJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseId = table.Column<string>(type: "TEXT", nullable: true),
                    FinishReason = table.Column<string>(type: "TEXT", nullable: true),
                    RequestSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ResponseSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Snapshot = table.Column<string>(type: "TEXT", nullable: false),
                    Failure = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionRuns_Requests_AiRequestId",
                        column: x => x.AiRequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Sha256",
                table: "Attachments",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_WorkspaceId",
                table: "Attachments",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPlanDependencies_ExecutionPlanId",
                table: "ExecutionPlanDependencies",
                column: "ExecutionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPlanRequests_ExecutionPlanId",
                table: "ExecutionPlanRequests",
                column: "ExecutionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPlanRuns_ExecutionPlanId",
                table: "ExecutionPlanRuns",
                column: "ExecutionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPlans_WorkspaceId",
                table: "ExecutionPlans",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_AiRequestId",
                table: "ExecutionRuns",
                column: "AiRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_WorkspaceId",
                table: "Requests",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceVariables_WorkspaceId_Name",
                table: "WorkspaceVariables",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "ExecutionPlanDependencies");

            migrationBuilder.DropTable(
                name: "ExecutionPlanRequests");

            migrationBuilder.DropTable(
                name: "ExecutionPlanRuns");

            migrationBuilder.DropTable(
                name: "ExecutionRuns");

            migrationBuilder.DropTable(
                name: "WorkspaceVariables");

            migrationBuilder.DropTable(
                name: "ExecutionPlans");

            migrationBuilder.DropTable(
                name: "Requests");

            migrationBuilder.DropTable(
                name: "Workspaces");
        }
    }
}
