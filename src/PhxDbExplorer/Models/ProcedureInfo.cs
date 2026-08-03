namespace PhxDbExplorer.Models;

public record ProcedureInfo(
    string SchemaName,
    string ProcedureName,
    DateTime? Created,
    DateTime? LastAltered
);

public record ProcedureParameter(
    string ParameterName,
    string DataType,
    bool IsOutput,
    bool HasDefault
);

public record ProcedureDefinition(
    string SchemaName,
    string ProcedureName,
    string? Definition,
    IReadOnlyList<ProcedureParameter> Parameters
);
