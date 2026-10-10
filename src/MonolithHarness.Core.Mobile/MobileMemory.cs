using Microsoft.Data.Sqlite;
namespace MonolithHarness.Core.Mobile;

public sealed record MobileMemoryEntry(long Id,string Scope,string Content,string Updated);
public sealed class MobileMemory(string directory,int projectId,int chatId,bool shared)
{
    async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var db=new SqliteConnection("Data Source="+Path.Combine(directory,"mobile-memory.sqlite"));
        try {await db.OpenAsync(ct);using var command=db.CreateCommand();command.CommandText="CREATE TABLE IF NOT EXISTS Memories (Id INTEGER PRIMARY KEY, Scope TEXT NOT NULL, Owner INTEGER NOT NULL, Content TEXT NOT NULL, Updated TEXT NOT NULL); CREATE INDEX IF NOT EXISTS IX_Memories_Scope ON Memories(Scope,Owner);";await command.ExecuteNonQueryAsync(ct);return db;}
        catch{await db.DisposeAsync();throw;}
    }
    string Visible=>"((Scope='conversation' AND Owner=@chat) OR (Scope='project' AND Owner=@project)"+(shared?" OR (Scope='shared' AND Owner=0)":"")+")";
    void Owners(SqliteCommand command){command.Parameters.AddWithValue("@chat",chatId);command.Parameters.AddWithValue("@project",projectId);}
    public async Task<IReadOnlyList<MobileMemoryEntry>> SearchAsync(string query,CancellationToken ct=default)
    {
        await using var db=await OpenAsync(ct);using var command=db.CreateCommand();
        command.CommandText="SELECT Id,Scope,Content,Updated FROM Memories WHERE "+Visible+" AND instr(lower(Content),lower(@query))>0 ORDER BY Id DESC LIMIT 50";
        Owners(command);command.Parameters.AddWithValue("@query",query.Length>1000?query[..1000]:query);using var reader=await command.ExecuteReaderAsync(ct);var list=new List<MobileMemoryEntry>();
        while(await reader.ReadAsync(ct))list.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3)));return list;
    }
    public async Task<long> SaveAsync(string scope,string content,CancellationToken ct=default)
    {
        var owner=scope switch {"conversation" when chatId>0=>chatId,"project" when projectId>0=>projectId,"shared" when shared=>0,_=>throw new UnauthorizedAccessException("Portée mémoire indisponible.")};
        if(string.IsNullOrWhiteSpace(content) || content.Length>4000)throw new ArgumentException("La mémoire doit contenir entre 1 et 4000 caractères.");
        await using var db=await OpenAsync(ct);using var command=db.CreateCommand();Owners(command);
        command.CommandText="SELECT count(*) FROM Memories WHERE Scope=@scope AND Owner=@owner";command.Parameters.AddWithValue("@scope",scope);command.Parameters.AddWithValue("@owner",owner);
        if(Convert.ToInt64(await command.ExecuteScalarAsync(ct))>=500)throw new IOException("Maximum 500 souvenirs par portée. Supprimez les entrées inutiles.");
        command.CommandText="INSERT INTO Memories(Scope,Owner,Content,Updated) VALUES (@scope,@owner,@content,@updated); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("@content",content.Trim());command.Parameters.AddWithValue("@updated",DateTime.UtcNow.ToString("O"));return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }
    public async Task<MobileMemoryEntry?> FindAsync(long id,CancellationToken ct=default)
    {
        await using var db=await OpenAsync(ct);using var command=db.CreateCommand();Owners(command);command.Parameters.AddWithValue("@id",id);
        command.CommandText="SELECT Id,Scope,Content,Updated FROM Memories WHERE Id=@id AND "+Visible;
        using var reader=await command.ExecuteReaderAsync(ct);return await reader.ReadAsync(ct)?new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3)):null;
    }
    public async Task DeleteAsync(long id,CancellationToken ct=default)
    {
        await using var db=await OpenAsync(ct);using var command=db.CreateCommand();Owners(command);command.Parameters.AddWithValue("@id",id);
        command.CommandText="DELETE FROM Memories WHERE Id=@id AND "+Visible;
        if(await command.ExecuteNonQueryAsync(ct)==0)throw new UnauthorizedAccessException("Souvenir introuvable dans les portées autorisées.");
    }
}
