namespace PhxDbExplorer.Models;

public record ConstraintInfo(
    string ConstraintName,
    string ConstraintType,
    string? CheckClause
);
