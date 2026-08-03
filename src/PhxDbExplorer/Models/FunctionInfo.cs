namespace PhxDbExplorer.Models;

public record FunctionInfo(
    string SchemaName,
    string FunctionName,
    string FunctionType,
    string? ReturnType,
    DateTime? Created,
    DateTime? LastAltered
);

public record FunctionParameter(
    string ParameterName,
    string DataType,
    bool IsOutput
);

public record FunctionDefinition(
    string SchemaName,
    string FunctionName,
    string? Definition,
    IReadOnlyList<FunctionParameter> Parameters
);
