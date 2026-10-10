using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
namespace MonolithHarness.Core.Mobile;

public sealed class MobileConversationStore(string directory) : IConversationStore
{
    readonly string database = Path.Combine(directory, "mobile.sqlite");
    MobileDb Open() => new(database);
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        await using var db = Open(); await db.Database.EnsureCreatedAsync(ct);
        if (!await db.Projects.AnyAsync(ct)) { db.Projects.Add(new Project { Name = "Mes discussions" }); await db.SaveChangesAsync(ct); }
    }
    public async Task<List<Project>> ProjectsAsync(CancellationToken ct = default) { await using var db=Open(); return await db.Projects.OrderBy(x=>x.Id).ToListAsync(ct); }
    public async Task<List<Chat>> ChatsAsync(int projectId, CancellationToken ct = default) { await using var db=Open(); return await db.Chats.Where(x=>x.ProjectId==projectId).OrderByDescending(x=>x.UpdatedUtc).ToListAsync(ct); }
    public async Task<List<Message>> MessagesAsync(int chatId, CancellationToken ct = default) { await using var db=Open(); return await db.Messages.Include(x=>x.Attachments).Where(x=>x.ChatId==chatId).OrderBy(x=>x.Id).ToListAsync(ct); }
    public async Task<List<Provider>> ProvidersAsync(CancellationToken ct = default) { await using var db=Open(); return await db.Providers.OrderBy(x=>x.Id).ToListAsync(ct); }
    public async Task<Project> CreateProjectAsync(string name, CancellationToken ct = default) { await using var db=Open(); var project=new Project {Name=name.Trim()}; if(project.Name.Length==0) throw new ArgumentException("Le nom du projet est requis."); db.Add(project); await db.SaveChangesAsync(ct); return project; }
    public async Task<Chat> CreateChatAsync(int projectId, CancellationToken ct = default) { await using var db=Open(); var chat=new Chat {ProjectId=projectId,InteractionMode="chat",ChatWebEnabled=false}; db.Add(chat); await db.SaveChangesAsync(ct); return chat; }
    public async Task SaveProviderAsync(Provider provider, CancellationToken ct = default)
    {
        if(provider.IsLocal || provider.IsExternalAgent || provider.IsComposite) throw new PlatformNotSupportedException("La version Android utilise des fournisseurs API.");
        await using var db=Open(); db.Update(provider); await db.SaveChangesAsync(ct);
    }
    public async Task SaveMessageAsync(Message message, CancellationToken ct = default)
    {
        await using var db=Open(); db.Update(message);
        var chat=await db.Chats.FindAsync([message.ChatId],ct);
        if(chat!=null) { chat.UpdatedUtc=DateTime.UtcNow; if(message.Role=="user" && chat.Title=="Nouvelle conversation") chat.Title=message.Content[..Math.Min(55,message.Content.Length)].Replace('\n',' '); }
        await db.SaveChangesAsync(ct);
    }
    sealed class MobileDb(string path) : DbContext
    {
        public DbSet<Project> Projects=>Set<Project>(); public DbSet<Chat> Chats=>Set<Chat>();
        public DbSet<Message> Messages=>Set<Message>(); public DbSet<Provider> Providers=>Set<Provider>();
        protected override void OnConfiguring(DbContextOptionsBuilder options)=>options.UseSqlite($"Data Source={path}");
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Provider>().Ignore(x=>x.ContextLimit); // Store per-model context settings in ModelContextsJson.
            model.Entity<Project>().HasMany(x=>x.Chats).WithOne().HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            model.Entity<Chat>().HasMany(x=>x.Messages).WithOne().HasForeignKey(x=>x.ChatId).OnDelete(DeleteBehavior.Cascade);
            model.Entity<Message>().HasMany(x=>x.Attachments).WithOne().HasForeignKey(x=>x.MessageId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
