using System.Data.Common;
using PhxDbExplorer.Models;

namespace PhxDbExplorer.Providers;

/// <summary>
/// Materialises a <see cref="DbDataReader"/> into a <see cref="QueryResult"/>, stopping at the
/// row limit. Limiting here rather than by rewriting the caller's SQL keeps the cap engine-neutral
/// and safe for statements that already carry their own TOP/LIMIT/ORDER BY.
/// </summary>
internal static class QueryResultReader
{
    /// <summary>Longest binary value rendered in full before it is summarised by length.</summary>
    private const int MaxBinaryBytes = 64;

    public static async Task<QueryResult> ReadAsync(
        DbDataReader reader, int rowLimit, CancellationToken ct)
    {
        var columns = new List<QueryColumn>(reader.FieldCount);
        var names = new string[reader.FieldCount];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (string.IsNullOrEmpty(name))
                name = $"column{i + 1}";
            // A query may project the same name twice (or none at all); keep every column addressable
            if (!used.Add(name))
            {
                var unique = $"{name}_{i + 1}";
                while (!used.Add(unique))
                    unique += "_";
                name = unique;
            }
            names[i] = name;
            columns.Add(new QueryColumn(name, reader.GetDataTypeName(i)));
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var truncated = false;

        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= rowLimit)
            {
                truncated = true;
                break;
            }

            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
                row[names[i]] = await reader.IsDBNullAsync(i, ct) ? null : Normalize(reader.GetValue(i));
            rows.Add(row);
        }

        return new QueryResult(columns, rows, rows.Count, rowLimit, truncated);
    }

    /// <summary>Converts a provider value into something that serialises predictably as JSON.</summary>
    private static object? Normalize(object value) => value switch
    {
        DBNull => null,
        string or bool or byte or short or int or long or float or double or decimal => value,
        sbyte or ushort or uint or ulong => value,
        byte[] bytes => FormatBinary(bytes),
        DateTime dt => dt.ToString("O"),
        DateTimeOffset dto => dto.ToString("O"),
        DateOnly d => d.ToString("O"),
        TimeOnly t => t.ToString("O"),
        TimeSpan ts => ts.ToString(),
        Guid g => g.ToString(),
        // Engine-specific types (spatial, ranges, arrays, …) have no stable JSON shape
        _ => value.ToString()
    };

    private static string FormatBinary(byte[] bytes) =>
        bytes.Length <= MaxBinaryBytes
            ? $"0x{Convert.ToHexString(bytes)}"
            : $"0x{Convert.ToHexString(bytes.AsSpan(0, MaxBinaryBytes))}… ({bytes.Length} bytes)";
}
