namespace PhxDbExplorer.Models;

public record IndexInfo(
    string IndexName,
    string IndexType,
    bool IsUnique,
    bool IsPrimaryKey,
    IReadOnlyList<string> Columns
);
