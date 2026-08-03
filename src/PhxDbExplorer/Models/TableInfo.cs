namespace PhxDbExplorer.Models;

public record TableInfo(
    string SchemaName,
    string TableName,
    string TableType,
    string? Description
);
