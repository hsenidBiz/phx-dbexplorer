using System.Data;
using Microsoft.Data.SqlClient;
using PhxDbExplorer.Configuration;
using PhxDbExplorer.Models;

namespace PhxDbExplorer.Providers;

public sealed class SqlServerSchemaProvider(DatabaseConfig config) : ISchemaProvider
{
    private SqlConnection CreateConnection() => new(config.ConnectionString);

    private string SchemaInClause(string paramPrefix, out Dictionary<string, string> paramMap)
    {
        paramMap = config.SchemaFilter
            .Select((s, i) => (Key: $"@{paramPrefix}{i}", Value: s))
            .ToDictionary(x => x.Key, x => x.Value);
        return string.Join(", ", paramMap.Keys);
    }

    private static void AddSchemaParams(SqlCommand cmd, Dictionary<string, string> paramMap)
    {
        foreach (var kv in paramMap)
            cmd.Parameters.AddWithValue(kv.Key, kv.Value);
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        var inClause = SchemaInClause("s", out var paramMap);
        var sql = $"""
            SELECT
                t.TABLE_SCHEMA,
                t.TABLE_NAME,
                t.TABLE_TYPE,
                CAST(ep.value AS NVARCHAR(MAX)) AS DESCRIPTION
            FROM INFORMATION_SCHEMA.TABLES t
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = OBJECT_ID(QUOTENAME(t.TABLE_SCHEMA) + '.' + QUOTENAME(t.TABLE_NAME))
                AND ep.minor_id = 0
                AND ep.name = 'MS_Description'
                AND ep.class = 1
            WHERE t.TABLE_SCHEMA IN ({inClause})
            ORDER BY t.TABLE_SCHEMA, t.TABLE_TYPE, t.TABLE_NAME
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        AddSchemaParams(cmd, paramMap);

        var results = new List<TableInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TableInfo(
                reader.GetString("TABLE_SCHEMA"),
                reader.GetString("TABLE_NAME"),
                reader.GetString("TABLE_TYPE"),
                reader.IsDBNull("DESCRIPTION") ? null : reader.GetString("DESCRIPTION")));
        }
        return results;
    }

    public async Task<TableSchemaResult> GetTableSchemaAsync(
        string tableName,
        string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        var schema = schemaName ?? config.SchemaFilter.FirstOrDefault() ?? "dbo";

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var columns = await GetColumnsAsync(conn, schema, tableName, cancellationToken);
        var primaryKeys = await GetPrimaryKeyColumnsAsync(conn, schema, tableName, cancellationToken);
        var foreignKeys = await GetForeignKeysAsync(conn, schema, tableName, cancellationToken);
        var indexes = await GetIndexesAsync(conn, schema, tableName, cancellationToken);
        var constraints = await GetConstraintsAsync(conn, schema, tableName, cancellationToken);

        var pkSet = new HashSet<string>(primaryKeys, StringComparer.OrdinalIgnoreCase);
        var enrichedColumns = columns
            .Select(c => c with { IsPrimaryKey = pkSet.Contains(c.ColumnName) })
            .ToList();

        var tableType = await GetTableTypeAsync(conn, schema, tableName, cancellationToken);

        return new TableSchemaResult(schema, tableName, tableType, enrichedColumns, foreignKeys, indexes, constraints);
    }

    private static async Task<string> GetTableTypeAsync(
        SqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string ?? "BASE TABLE";
    }

    private static async Task<List<ColumnInfo>> GetColumnsAsync(
        SqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                c.IS_NULLABLE,
                c.COLUMN_DEFAULT,
                COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') AS IS_IDENTITY,
                CAST(ep.value AS NVARCHAR(MAX)) AS DESCRIPTION
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME))
                AND ep.minor_id = c.ORDINAL_POSITION
                AND ep.name = 'MS_Description'
                AND ep.class = 1
            WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
            ORDER BY c.ORDINAL_POSITION
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var results = new List<ColumnInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var dataType = reader.GetString("DATA_TYPE");
            var fullType = BuildFullType(dataType,
                reader.IsDBNull("CHARACTER_MAXIMUM_LENGTH") ? null : reader.GetInt32("CHARACTER_MAXIMUM_LENGTH"),
                reader.IsDBNull("NUMERIC_PRECISION") ? null : (int?)reader.GetByte("NUMERIC_PRECISION"),
                reader.IsDBNull("NUMERIC_SCALE") ? null : reader.GetInt32("NUMERIC_SCALE"));

            results.Add(new ColumnInfo(
                ColumnName: reader.GetString("COLUMN_NAME"),
                DataType: dataType,
                FullDataType: fullType,
                IsNullable: reader.GetString("IS_NULLABLE") == "YES",
                DefaultValue: reader.IsDBNull("COLUMN_DEFAULT") ? null : reader.GetString("COLUMN_DEFAULT"),
                IsIdentity: reader.IsDBNull("IS_IDENTITY") ? false : reader.GetInt32("IS_IDENTITY") == 1,
                IsPrimaryKey: false,
                Description: reader.IsDBNull("DESCRIPTION") ? null : reader.GetString("DESCRIPTION")));
        }
        return results;
    }

    private static string BuildFullType(string dataType, int? maxLength, int? precision, int? scale)
    {
        if (maxLength.HasValue && maxLength.Value == -1)
            return $"{dataType}(MAX)";
        if (maxLength.HasValue && maxLength.Value > 0)
            return $"{dataType}({maxLength.Value})";
        if (precision.HasValue && scale.HasValue && scale.Value > 0)
            return $"{dataType}({precision.Value},{scale.Value})";
        if (precision.HasValue && precision.Value > 0)
            return $"{dataType}({precision.Value})";
        return dataType;
    }

    private static async Task<List<string>> GetPrimaryKeyColumnsAsync(
        SqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT kcu.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                AND tc.TABLE_SCHEMA = @schema
                AND tc.TABLE_NAME = @table
            ORDER BY kcu.ORDINAL_POSITION
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(reader.GetString(0));
        return results;
    }

    private static async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(
        SqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT
                fk.name AS CONSTRAINT_NAME,
                COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS COLUMN_NAME,
                OBJECT_SCHEMA_NAME(fkc.referenced_object_id) AS REFERENCED_SCHEMA,
                OBJECT_NAME(fkc.referenced_object_id) AS REFERENCED_TABLE,
                COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS REFERENCED_COLUMN,
                fk.delete_referential_action_desc AS DELETE_ACTION,
                fk.update_referential_action_desc AS UPDATE_ACTION
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            WHERE OBJECT_SCHEMA_NAME(fk.parent_object_id) = @schema
                AND OBJECT_NAME(fk.parent_object_id) = @table
            ORDER BY fk.name, fkc.constraint_column_id
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var results = new List<ForeignKeyInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ForeignKeyInfo(
                reader.GetString("CONSTRAINT_NAME"),
                reader.GetString("COLUMN_NAME"),
                reader.GetString("REFERENCED_SCHEMA"),
                reader.GetString("REFERENCED_TABLE"),
                reader.GetString("REFERENCED_COLUMN"),
                reader.GetString("DELETE_ACTION"),
                reader.GetString("UPDATE_ACTION")));
        }
        return results;
    }

    private static async Task<List<IndexInfo>> GetIndexesAsync(
        SqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT
                i.name AS INDEX_NAME,
                i.type_desc AS INDEX_TYPE,
                i.is_unique,
                i.is_primary_key,
                c.name AS COLUMN_NAME,
                ic.key_ordinal
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE OBJECT_SCHEMA_NAME(i.object_id) = @schema
                AND OBJECT_NAME(i.object_id) = @table
                AND i.name IS NOT NULL
                AND ic.is_included_column = 0
            ORDER BY i.name, ic.key_ordinal
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var indexGroups = new Dictionary<string, (string Type, bool IsUnique, bool IsPk, List<string> Cols)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString("INDEX_NAME");
            if (!indexGroups.ContainsKey(name))
                indexGroups[name] = (reader.GetString("INDEX_TYPE"), reader.GetBoolean("is_unique"), reader.GetBoolean("is_primary_key"), []);
            indexGroups[name].Cols.Add(reader.GetString("COLUMN_NAME"));
        }

        return indexGroups.Select(kv =>
            new IndexInfo(kv.Key, kv.Value.Type, kv.Value.IsUnique, kv.Value.IsPk, kv.Value.Cols)).ToList();
    }

    private static async Task<List<ConstraintInfo>> GetConstraintsAsync(
        SqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT
                tc.CONSTRAINT_NAME,
                tc.CONSTRAINT_TYPE,
                cc.CHECK_CLAUSE
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            LEFT JOIN INFORMATION_SCHEMA.CHECK_CONSTRAINTS cc
                ON tc.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
            WHERE tc.TABLE_SCHEMA = @schema AND tc.TABLE_NAME = @table
                AND tc.CONSTRAINT_TYPE IN ('CHECK', 'UNIQUE')
            ORDER BY tc.CONSTRAINT_TYPE, tc.CONSTRAINT_NAME
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var results = new List<ConstraintInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ConstraintInfo(
                reader.GetString("CONSTRAINT_NAME"),
                reader.GetString("CONSTRAINT_TYPE"),
                reader.IsDBNull("CHECK_CLAUSE") ? null : reader.GetString("CHECK_CLAUSE")));
        }
        return results;
    }

    public async Task<IReadOnlyList<ProcedureInfo>> ListStoredProceduresAsync(CancellationToken cancellationToken = default)
    {
        var inClause = SchemaInClause("s", out var paramMap);
        var sql = $"""
            SELECT ROUTINE_SCHEMA, ROUTINE_NAME, CREATED, LAST_ALTERED
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'PROCEDURE'
                AND ROUTINE_SCHEMA IN ({inClause})
            ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        AddSchemaParams(cmd, paramMap);

        var results = new List<ProcedureInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ProcedureInfo(
                reader.GetString("ROUTINE_SCHEMA"),
                reader.GetString("ROUTINE_NAME"),
                reader.IsDBNull("CREATED") ? null : reader.GetDateTime("CREATED"),
                reader.IsDBNull("LAST_ALTERED") ? null : reader.GetDateTime("LAST_ALTERED")));
        }
        return results;
    }

    public async Task<ProcedureDefinition?> GetProcedureDefinitionAsync(
        string procedureName,
        string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        var schema = schemaName ?? config.SchemaFilter.FirstOrDefault() ?? "dbo";

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        const string paramSql = """
            SELECT
                p.name AS PARAMETER_NAME,
                t.name AS DATA_TYPE,
                p.is_output
            FROM sys.parameters p
            JOIN sys.types t ON p.user_type_id = t.user_type_id
            WHERE OBJECT_SCHEMA_NAME(p.object_id) = @schema
                AND OBJECT_NAME(p.object_id) = @procedure
                AND p.parameter_id > 0
            ORDER BY p.parameter_id
            """;

        await using var paramCmd = new SqlCommand(paramSql, conn);
        paramCmd.Parameters.AddWithValue("@schema", schema);
        paramCmd.Parameters.AddWithValue("@procedure", procedureName);

        var parameters = new List<ProcedureParameter>();
        {
            await using var paramReader = await paramCmd.ExecuteReaderAsync(cancellationToken);
            while (await paramReader.ReadAsync(cancellationToken))
            {
                parameters.Add(new ProcedureParameter(
                    paramReader.GetString("PARAMETER_NAME"),
                    paramReader.GetString("DATA_TYPE"),
                    paramReader.GetBoolean("is_output"),
                    false));
            }
        } // reader disposed here so the second command can use the same connection

        const string defSql = "SELECT OBJECT_DEFINITION(OBJECT_ID(QUOTENAME(@schema) + '.' + QUOTENAME(@name))) AS DEFINITION";
        await using var defCmd = new SqlCommand(defSql, conn);
        defCmd.Parameters.AddWithValue("@schema", schema);
        defCmd.Parameters.AddWithValue("@name", procedureName);
        var definition = await defCmd.ExecuteScalarAsync(cancellationToken) as string;

        return new ProcedureDefinition(schema, procedureName, definition, parameters);
    }

    public async Task<IReadOnlyList<FunctionInfo>> ListFunctionsAsync(CancellationToken cancellationToken = default)
    {
        var inClause = SchemaInClause("s", out var paramMap);
        var sql = $"""
            SELECT ROUTINE_SCHEMA, ROUTINE_NAME, ROUTINE_TYPE, DATA_TYPE AS RETURN_TYPE, CREATED, LAST_ALTERED
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'FUNCTION'
                AND ROUTINE_SCHEMA IN ({inClause})
            ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        AddSchemaParams(cmd, paramMap);

        var results = new List<FunctionInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new FunctionInfo(
                reader.GetString("ROUTINE_SCHEMA"),
                reader.GetString("ROUTINE_NAME"),
                reader.GetString("ROUTINE_TYPE"),
                reader.IsDBNull("RETURN_TYPE") ? null : reader.GetString("RETURN_TYPE"),
                reader.IsDBNull("CREATED") ? null : reader.GetDateTime("CREATED"),
                reader.IsDBNull("LAST_ALTERED") ? null : reader.GetDateTime("LAST_ALTERED")));
        }
        return results;
    }

    public async Task<FunctionDefinition?> GetFunctionDefinitionAsync(
        string functionName,
        string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        var schema = schemaName ?? config.SchemaFilter.FirstOrDefault() ?? "dbo";

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        const string paramSql = """
            SELECT
                p.name AS PARAMETER_NAME,
                t.name AS DATA_TYPE,
                p.is_output
            FROM sys.parameters p
            JOIN sys.types t ON p.user_type_id = t.user_type_id
            WHERE OBJECT_SCHEMA_NAME(p.object_id) = @schema
                AND OBJECT_NAME(p.object_id) = @function
                AND p.parameter_id > 0
            ORDER BY p.parameter_id
            """;

        await using var paramCmd = new SqlCommand(paramSql, conn);
        paramCmd.Parameters.AddWithValue("@schema", schema);
        paramCmd.Parameters.AddWithValue("@function", functionName);

        var parameters = new List<FunctionParameter>();
        {
            await using var paramReader = await paramCmd.ExecuteReaderAsync(cancellationToken);
            while (await paramReader.ReadAsync(cancellationToken))
            {
                parameters.Add(new FunctionParameter(
                    paramReader.GetString("PARAMETER_NAME"),
                    paramReader.GetString("DATA_TYPE"),
                    paramReader.GetBoolean("is_output")));
            }
        } // reader disposed here so the second command can use the same connection

        const string defSql = "SELECT OBJECT_DEFINITION(OBJECT_ID(QUOTENAME(@schema) + '.' + QUOTENAME(@name))) AS DEFINITION";
        await using var defCmd = new SqlCommand(defSql, conn);
        defCmd.Parameters.AddWithValue("@schema", schema);
        defCmd.Parameters.AddWithValue("@name", functionName);
        var definition = await defCmd.ExecuteScalarAsync(cancellationToken) as string;

        return new FunctionDefinition(schema, functionName, definition, parameters);
    }

    public async Task<SearchResult> SearchSchemaAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var inClause = SchemaInClause("s", out var schemaParams);
        var likeKeyword = $"%{keyword}%";

        var sql = $"""
            SELECT 'TABLE' AS OBJECT_TYPE, TABLE_SCHEMA, TABLE_NAME, NULL AS DETAIL
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA IN ({inClause}) AND TABLE_NAME LIKE @kw
            UNION ALL
            SELECT 'COLUMN', TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA IN ({inClause}) AND (TABLE_NAME LIKE @kw OR COLUMN_NAME LIKE @kw)
            UNION ALL
            SELECT ROUTINE_TYPE, ROUTINE_SCHEMA, ROUTINE_NAME, NULL
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_SCHEMA IN ({inClause}) AND ROUTINE_NAME LIKE @kw
            ORDER BY OBJECT_TYPE, TABLE_SCHEMA, TABLE_NAME
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        AddSchemaParams(cmd, schemaParams);
        cmd.Parameters.AddWithValue("@kw", likeKeyword);

        var matches = new List<SearchMatch>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new SearchMatch(
                reader.GetString("OBJECT_TYPE"),
                reader.GetString("TABLE_SCHEMA"),
                reader.GetString("TABLE_NAME"),
                reader.IsDBNull("DETAIL") ? null : reader.GetString("DETAIL")));
        }
        return new SearchResult(keyword, matches);
    }
}
