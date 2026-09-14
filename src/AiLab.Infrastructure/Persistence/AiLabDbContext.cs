using AiLab.Core.Execution;
using AiLab.Core.ExecutionPlans;
using AiLab.Core.Models;
using AiLab.Core.Requests;
using AiLab.Core.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Infrastructure.Persistence;

public sealed class AiLabDbContext(DbContextOptions<AiLabDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<WorkspaceVariable> WorkspaceVariables => Set<WorkspaceVariable>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<AiRequestDefinition> Requests => Set<AiRequestDefinition>();

    public DbSet<ExecutionRun> ExecutionRuns => Set<ExecutionRun>();

    public DbSet<ExecutionPlan> ExecutionPlans => Set<ExecutionPlan>();

    public DbSet<ExecutionPlanRun> ExecutionPlanRuns => Set<ExecutionPlanRun>();

    public DbSet<ProviderModel> ProviderModels => Set<ProviderModel>();

    public DbSet<ModelCatalogProviderStatus> ModelCatalogProviderStatuses => Set<ModelCatalogProviderStatus>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Applies to every DateTimeOffset(/?) property in the model — see the converter's doc comment.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToUtcDateTimeConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableDateTimeOffsetToUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>(builder =>
        {
            builder.HasKey(w => w.Id);
            builder.Property(w => w.Name).IsRequired();
            builder.Property(w => w.Tags).HasConversion(JsonValueConverter.StringList, JsonValueConverter.StringListComparer);
        });

        modelBuilder.Entity<WorkspaceVariable>(builder =>
        {
            builder.HasKey(v => v.Id);
            builder.HasOne<Workspace>().WithMany().HasForeignKey(v => v.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(v => new { v.WorkspaceId, v.Name }).IsUnique();
        });

        modelBuilder.Entity<Attachment>(builder =>
        {
            builder.HasKey(a => a.Id);
            builder.HasOne<Workspace>().WithMany().HasForeignKey(a => a.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(a => a.Sha256);
        });

        modelBuilder.Entity<AiRequestDefinition>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.HasOne<Workspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);

            builder.OwnsOne(r => r.CachedContext, cc =>
            {
                cc.Property(c => c.AttachmentIds).HasConversion(JsonValueConverter.GuidList, JsonValueConverter.GuidListComparer);
            });
            builder.OwnsOne(r => r.UserContext, uc =>
            {
                uc.Property(c => c.AttachmentIds).HasConversion(JsonValueConverter.GuidList, JsonValueConverter.GuidListComparer);
            });
            builder.Navigation(r => r.CachedContext).IsRequired();
            builder.Navigation(r => r.UserContext).IsRequired();

            builder.OwnsOne(r => r.Reasoning);
            builder.OwnsOne(r => r.RetryPolicy);
            builder.Navigation(r => r.RetryPolicy).IsRequired();

            builder.Property(r => r.InputBindings).HasConversion(JsonValueConverter.InputBindingList, JsonValueConverter.InputBindingListComparer);
            builder.Property(r => r.ProviderSettings).HasConversion(JsonValueConverter.StringDictionary, JsonValueConverter.StringDictionaryComparer);
            builder.Property(r => r.Tags).HasConversion(JsonValueConverter.StringList, JsonValueConverter.StringListComparer);
        });

        modelBuilder.Entity<ExecutionRun>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.HasOne<AiRequestDefinition>().WithMany().HasForeignKey(r => r.AiRequestId).OnDelete(DeleteBehavior.Cascade);

            builder.OwnsOne(r => r.Usage);
            builder.Navigation(r => r.Usage).IsRequired();

            builder.OwnsOne(r => r.Failure, f =>
            {
                f.ToJson();
            });

            builder.Property(r => r.Snapshot).HasConversion(JsonValueConverter.ForObject<RequestSnapshot>());
        });

        modelBuilder.Entity<ExecutionPlan>(builder =>
        {
            builder.HasKey(p => p.Id);
            builder.HasOne<Workspace>().WithMany().HasForeignKey(p => p.WorkspaceId).OnDelete(DeleteBehavior.Cascade);

            builder.OwnsMany(p => p.Requests, r =>
            {
                r.WithOwner().HasForeignKey("ExecutionPlanId");
                r.Property<int>("Id");
                r.HasKey("Id");
                r.ToTable("ExecutionPlanRequests");
            });

            builder.OwnsMany(p => p.Dependencies, d =>
            {
                d.WithOwner().HasForeignKey("ExecutionPlanId");
                d.Property<int>("Id");
                d.HasKey("Id");
                d.ToTable("ExecutionPlanDependencies");
            });
        });

        modelBuilder.Entity<ExecutionPlanRun>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.HasOne<ExecutionPlan>().WithMany().HasForeignKey(r => r.ExecutionPlanId).OnDelete(DeleteBehavior.Cascade);

            builder.Property(r => r.Groups).HasConversion(JsonValueConverter.ForObject<IReadOnlyList<ExecutionGroupRun>>(), JsonValueConverter.ObjectComparer<IReadOnlyList<ExecutionGroupRun>>());
            builder.Property(r => r.ExecutionRunIds).HasConversion(JsonValueConverter.GuidList, JsonValueConverter.GuidListComparer);
        });

        modelBuilder.Entity<ProviderModel>(builder =>
        {
            builder.HasKey(m => new { m.ProviderId, m.ModelId });
            builder.Property(m => m.SupportedReasoningLevels).HasConversion(JsonValueConverter.StringList, JsonValueConverter.StringListComparer);
            builder.Property(m => m.InputModalities).HasConversion(JsonValueConverter.StringList, JsonValueConverter.StringListComparer);
            builder.Property(m => m.OutputModalities).HasConversion(JsonValueConverter.StringList, JsonValueConverter.StringListComparer);
        });

        modelBuilder.Entity<ModelCatalogProviderStatus>(builder =>
        {
            builder.HasKey(s => s.ProviderId);
        });
    }
}
