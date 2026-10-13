using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MonolithHarness.Server.Data;

public sealed class ServerDb(DbContextOptions<ServerDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<RecoveryToken> RecoveryTokens => Set<RecoveryToken>();
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<SharedProvider> Providers => Set<SharedProvider>();
    public DbSet<ProviderAccess> ProviderAccesses => Set<ProviderAccess>();
    public DbSet<CloudProject> Projects => Set<CloudProject>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<RunRequest> Runs => Set<RunRequest>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<CloudSchedule> Schedules => Set<CloudSchedule>();
    public DbSet<ScheduleOccurrence> Occurrences => Set<ScheduleOccurrence>();
    public DbSet<SharedConversation> Shares => Set<SharedConversation>();
    public DbSet<ShareParticipant> ShareParticipants => Set<ShareParticipant>();
    public DbSet<MessageAuthor> MessageAuthors => Set<MessageAuthor>();
    public DbSet<UsageSample> UsageSamples => Set<UsageSample>();

    public static DbContextOptions<ServerDb> Options(string database) => new DbContextOptionsBuilder<ServerDb>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = true }.ToString())
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
        .Options;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasMany(x => x.Roles).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Role>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Users).WithOne(x => x.Role).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        model.Entity<UserSession>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.RevokedUtc });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<RecoveryToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SettingEntry>().HasKey(x => x.Key);
        model.Entity<AuditEntry>(e =>
        {
            e.HasIndex(x => x.TimestampUtc);
            e.HasIndex(x => x.Action);
        });
        model.Entity<SharedProvider>().HasIndex(x => x.Name).IsUnique();
        model.Entity<ProviderAccess>(e =>
        {
            e.HasIndex(x => new { x.ProviderId, x.SubjectKind, x.SubjectId }).IsUnique();
            e.HasOne<SharedProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<CloudProject>(e =>
        {
            e.HasIndex(x => x.OwnerId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ProjectMember>(e =>
        {
            e.HasKey(x => new { x.ProjectId, x.UserId });
            e.HasOne<CloudProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<RunRequest>(e =>
        {
            e.HasIndex(x => x.State);
            e.HasIndex(x => x.Sequence).IsUnique();
            e.HasIndex(x => new { x.RequesterId, x.State });
            e.HasOne<CloudProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Approval>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.State });
            e.HasOne<RunRequest>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<CloudSchedule>(e =>
        {
            e.HasIndex(x => new { x.Enabled, x.NextRunUtc });
            e.HasOne<CloudProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ScheduleOccurrence>(e =>
        {
            e.HasKey(x => new { x.ScheduleId, x.OccurrenceUtc });
            e.HasOne<CloudSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SharedConversation>(e =>
        {
            e.HasIndex(x => x.HostId);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.IsPublic);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<MessageAuthor>(e =>
        {
            e.HasKey(x => new { x.ProjectId, x.MessageId });
            e.HasIndex(x => new { x.ProjectId, x.ChatId });
            e.HasOne<CloudProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<UsageSample>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.SampleKey }).IsUnique();
            e.HasIndex(x => x.CompletedUtc);
            e.HasIndex(x => new { x.UserId, x.CompletedUtc });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ShareParticipant>(e =>
        {
            e.HasKey(x => new { x.ShareId, x.UserId });
            e.HasOne<SharedConversation>().WithMany().HasForeignKey(x => x.ShareId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class ServerDbFactory : IDesignTimeDbContextFactory<ServerDb>
{
    public ServerDb CreateDbContext(string[] args) => new(ServerDb.Options(Path.Combine(Path.GetTempPath(), "monolith-server-design.sqlite")));
}
