namespace PhxDbExplorer.Models;

public record ForeignKeyInfo(
    string ConstraintName,
    string ColumnName,
    string ReferencedSchema,
    string ReferencedTable,
    string ReferencedColumn,
    string DeleteAction,
    string UpdateAction
);
