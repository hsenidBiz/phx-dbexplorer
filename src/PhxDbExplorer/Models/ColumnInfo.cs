namespace PhxDbExplorer.Models;

public record ColumnInfo(
    string ColumnName,
    string DataType,
    string FullDataType,
    bool IsNullable,
    string? DefaultValue,
    bool IsIdentity,
    bool IsPrimaryKey,
    string? Description
);
