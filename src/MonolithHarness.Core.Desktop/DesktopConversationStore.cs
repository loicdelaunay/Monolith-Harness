using Microsoft.EntityFrameworkCore;
namespace MonolithHarness.Core;

/// <summary>Desktop implementation of the shared store contract, using the existing database/migrations.</summary>
public sealed class DesktopConversationStore(string database) : IConversationStore
{
    HarnessDb Open()=>new(database);
    public async Task<List<Project>> ProjectsAsync(CancellationToken ct=default) {await using var db=Open();return await db.Projects.OrderBy(x=>x.Id).ToListAsync(ct);}
    public async Task<List<Chat>> ChatsAsync(int projectId,CancellationToken ct=default) {await using var db=Open();return await db.Chats.Where(x=>x.ProjectId==projectId).OrderByDescending(x=>x.UpdatedUtc).ToListAsync(ct);}
    public async Task<List<Message>> MessagesAsync(int chatId,CancellationToken ct=default) {await using var db=Open();return await db.Messages.Include(x=>x.Attachments).Where(x=>x.ChatId==chatId).OrderBy(x=>x.Id).ToListAsync(ct);}
    public async Task<List<Provider>> ProvidersAsync(CancellationToken ct=default) {await using var db=Open();return await db.Providers.OrderBy(x=>x.Id).ToListAsync(ct);}
    public async Task<Project> CreateProjectAsync(string name,CancellationToken ct=default) {await using var db=Open();var item=new Project {Name=name.Trim()};if(item.Name.Length==0)throw new ArgumentException("Le nom du projet est requis.");db.Add(item);await db.SaveChangesAsync(ct);return item;}
    public async Task<Chat> CreateChatAsync(int projectId,CancellationToken ct=default) {await using var db=Open();var item=new Chat {ProjectId=projectId};db.Add(item);await db.SaveChangesAsync(ct);return item;}
    public async Task SaveProviderAsync(Provider provider,CancellationToken ct=default) {await using var db=Open();db.Update(provider);await db.SaveChangesAsync(ct);}
    public async Task SaveMessageAsync(Message message,CancellationToken ct=default) {await using var db=Open();db.Update(message);await db.SaveChangesAsync(ct);}
}
