using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace OhMyHarness.Core;

public sealed record MemoryBrowserPage(List<MemoryEntry> Rows, int Total, int Matching, int Offset);

// Administrative GUI view. Model tools continue to use MemoryStore's scoped access checks.
public static class MemoryBrowser
{
    public static IReadOnlyList<string> Columns { get; } = [nameof(MemoryEntry.Id), nameof(MemoryEntry.Scope), nameof(MemoryEntry.Category),
        nameof(MemoryEntry.ProjectId), nameof(MemoryEntry.ChatId), nameof(MemoryEntry.Key), nameof(MemoryEntry.Title), nameof(MemoryEntry.Content),
        nameof(MemoryEntry.Tags), nameof(MemoryEntry.Partition), nameof(MemoryEntry.OriginChatId), nameof(MemoryEntry.Author), nameof(MemoryEntry.Version),
        nameof(MemoryEntry.CreatedUtc), nameof(MemoryEntry.UpdatedUtc)];
    public const int PageSize = 100;
    public static Task<MemoryBrowserPage> ReadAsync(string path, IReadOnlyDictionary<string, string> filters, string sort, bool descending, int offset, CancellationToken ct) => Task.Run(async () =>
    {
        if (!Columns.Contains(sort) || offset < 0) throw new ArgumentException("Tri ou page invalide / Invalid sort or page.");
        await using var db = new HarnessDb(path);
        var query = db.Memories.AsNoTracking();
        int total = await query.CountAsync(ct);
        foreach (var (column, raw) in filters)
        {
            if (!Columns.Contains(column)) throw new ArgumentException("Colonne inconnue / Unknown column.");
            var value = raw.Trim(); if (value.Length == 0) continue;
            var row = Expression.Parameter(typeof(MemoryEntry), "row");
            var property = Expression.Property(row, column);
            Expression predicate;
            if (property.Type == typeof(string))
                predicate = Expression.Call(Expression.Call(property, nameof(string.ToLower), Type.EmptyTypes), nameof(string.Contains), Type.EmptyTypes, Expression.Constant(value.ToLowerInvariant()));
            else if (property.Type == typeof(DateTime))
            {
                if (!DateTime.TryParseExact(value, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    throw new ArgumentException(column + " : date au format AAAA-MM-JJ / use YYYY-MM-DD.");
                predicate = Expression.AndAlso(Expression.GreaterThanOrEqual(property, Expression.Constant(day)), Expression.LessThan(property, Expression.Constant(day.AddDays(1))));
            }
            else if (property.Type == typeof(int?) && value.Equals("null", StringComparison.OrdinalIgnoreCase))
                predicate = Expression.Equal(property, Expression.Constant(null, typeof(int?)));
            else
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    throw new ArgumentException(column + " : nombre entier requis / integer required.");
                predicate = Expression.Equal(property, Expression.Convert(Expression.Constant(number), property.Type));
            }
            query = query.Where(Expression.Lambda<Func<MemoryEntry, bool>>(predicate, row));
        }
        int matching = await query.CountAsync(ct);
        offset = matching == 0 ? 0 : Math.Min(offset, (matching - 1) / PageSize * PageSize);
        var ordered = descending ? query.OrderByDescending(row => EF.Property<object>(row, sort)) : query.OrderBy(row => EF.Property<object>(row, sort));
        var rows = await ordered.ThenBy(row => row.Id).Skip(offset).Take(PageSize).ToListAsync(ct);
        return new MemoryBrowserPage(rows, total, matching, offset);
    }, ct);
    public static string Cell(MemoryEntry row, string column)
    {
        var value = typeof(MemoryEntry).GetProperty(column)?.GetValue(row);
        return value is DateTime date ? date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : value?.ToString() ?? "NULL";
    }
}
