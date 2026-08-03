namespace PhxDbExplorer.Models;

public record TableSchemaResult(
    string SchemaName,
    string TableName,
    string TableType,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ConstraintInfo> Constraints
);
