namespace PhxDbExplorer.Models;

public record QueryColumn(
    string Name,
    string DataType
);

public record QueryResult(
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowCount,
    int RowLimit,
    bool Truncated
);

public record TableRowCount(
    string SchemaName,
    string TableName,
    long RowCount
);
