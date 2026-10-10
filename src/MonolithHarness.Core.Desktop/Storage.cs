using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace MonolithHarness.Core;

public sealed class HarnessDb : DbContext
{
    public static string DataDirectory => PortableStorage.Root;
    public static string DatabasePath => Path.Combine(DataDirectory, "database.sqlite");
    readonly string path;
    public string FilePath => path;
    public HarnessDb(string? path = null)
    {
        this.path = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DatabasePath : path);
    }
    public DbSet<SubagentRecord> Subagents => Set<SubagentRecord>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<PendingInput> PendingInputs => Set<PendingInput>();
    public DbSet<RagChunk> RagChunks => Set<RagChunk>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<AppState> States => Set<AppState>();
    public DbSet<PromptTemplate> Templates => Set<PromptTemplate>();
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();
    public DbSet<ExternalChatSession> ExternalChatSessions => Set<ExternalChatSession>();
    public DbSet<McpServer> McpServers => Set<McpServer>();
    public DbSet<MemoryEntry> Memories => Set<MemoryEntry>();
    public DbSet<TokenUsage> TokenUsages => Set<TokenUsage>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path }.ToString())
               .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    protected override void OnModelCreating(ModelBuilder model)
    {
        // Materialization must restore the old column without creating a manual model override.
        model.Entity<Provider>().Property(x => x.ContextLimit).HasField("legacyContextLimit").UsePropertyAccessMode(PropertyAccessMode.Field);
        model.Entity<TokenUsage>().HasIndex(x => x.CompletedUtc);
        model.Entity<TokenUsage>().HasIndex(x => new { x.ProviderId, x.Model, x.CompletedUtc });
        model.Entity<TokenUsage>().Ignore(x => x.TotalTokens).Ignore(x => x.Estimated);
        model.Entity<MemoryEntry>().HasIndex(x => new { x.Partition, x.Category, x.Key }).IsUnique();
        model.Entity<MemoryEntry>().HasIndex(x => new { x.Scope, x.ProjectId, x.ChatId, x.UpdatedUtc });
        model.Entity<MemoryEntry>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<MemoryEntry>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<MemoryEntry>().HasOne<Chat>().WithMany().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<MemoryEntry>().HasOne<Chat>().WithMany().HasForeignKey(x => x.OriginChatId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<ScheduledTask>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ScheduledTask>().HasIndex(x => new { x.Enabled, x.NextRunUtc });
        model.Entity<PendingInput>().HasOne<Chat>().WithMany().HasForeignKey(x=>x.ChatId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PendingInput>().HasIndex(x=>new{x.ChatId,x.Id});
        model.Entity<SubagentRecord>().HasOne<Chat>().WithMany().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RagChunk>().HasIndex(x => new { x.ProjectId, x.Model });
        model.Entity<RagChunk>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Project>().HasMany(x => x.Chats).WithOne().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Chat>().HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Message>().HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PermissionGrant>().HasIndex(x => x.Scope).IsUnique();
        model.Entity<ExternalChatSession>().HasIndex(x => new { x.ChatId, x.ProviderId }).IsUnique();
        model.Entity<ExternalChatSession>().HasOne<Chat>().WithMany().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ExternalChatSession>().HasOne<Provider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Cascade);
    }
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        PortableStorage.MigrateLayout(Path.GetDirectoryName(path)!);
        await Database.MigrateAsync();
        if (!await States.AnyAsync())
        {
            if (!await Providers.AnyAsync()) Providers.AddRange(new Provider(), new Provider { Name = "DeepSeek", BaseUrl = "https://api.deepseek.com", Model = "deepseek-flash", SupportsImages = true });
            States.Add(new AppState { FeaturesJson = new FeatureSettings { WelcomeCompleted = false }.Json() });
            if (!await Projects.AnyAsync()) Projects.Add(new Project { Name = "Espace personnel", Chats = [new Chat()] });
            await SaveChangesAsync();
        }
        var legacyProviders = await Providers.Where(x => x.Kind == "").ToListAsync();
        if (legacyProviders.Count > 0)
        {
            foreach (var item in legacyProviders) item.Kind = "openai";
            await SaveChangesAsync();
        }
        await PortableStorage.RemapReferencesAsync(this);
    }

    public static async Task CopyDatabaseAsync(string sourcePath, string destinationPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destinationPath = Path.GetFullPath(destinationPath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Base SQLite source introuvable.", sourcePath);
        if (File.Exists(destinationPath)) throw new IOException("La base SQLite de destination existe déjà.");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporary = destinationPath + ".migrating-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString());
            await source.OpenAsync();
            await destination.OpenAsync();
            source.BackupDatabase(destination);
            await destination.CloseAsync();
            await source.CloseAsync();
            try { File.Move(temporary, destinationPath); }
            catch (IOException) when (File.Exists(destinationPath)) { }
        }
        catch (Exception ex)
        {
            throw new IOException($"Impossible de copier la base SQLite vers {destinationPath}.", ex);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var candidate = temporary + suffix;
                if (File.Exists(candidate)) try { File.Delete(candidate); } catch { }
            }
        }
    }
}
public sealed class DesignFactory : IDesignTimeDbContextFactory<HarnessDb>
{
    public HarnessDb CreateDbContext(string[] args) => new();
}
public static class KeyVault
{
    public static byte[] Encrypt(string key) => OperatingSystem.IsWindows()
        ? ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser)
        : OperatingSystem.IsMacOS() ? MacKeychain.Store(key) : LinuxKeyVault.Encrypt(key);
    public static string Decrypt(byte[] key)
    {
        if (key.Length == 0) return "";
        if (Encoding.UTF8.GetString(key).StartsWith(LinuxKeyVault.Prefix, StringComparison.Ordinal)) return LinuxKeyVault.Decrypt(key);
        if (Encoding.UTF8.GetString(key).StartsWith(MacKeychain.Prefix, StringComparison.Ordinal)) return MacKeychain.Read(key);
        if (OperatingSystem.IsWindows()) return Encoding.UTF8.GetString(ProtectedData.Unprotect(key, null, DataProtectionScope.CurrentUser));
        throw new InvalidOperationException("Ressaisissez cette clé sur ce système : son ancien coffre n’est pas disponible. / Re-enter this key on this OS.");
    }
}
