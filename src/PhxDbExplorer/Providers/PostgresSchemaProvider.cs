using Npgsql;
using PhxDbExplorer.Configuration;
using PhxDbExplorer.Models;

namespace PhxDbExplorer.Providers;

public sealed class PostgresSchemaProvider(DatabaseConfig config) : ISchemaProvider
{
    private NpgsqlConnection CreateConnection() => new(config.ConnectionString);

    private string SchemaInClause(string paramPrefix, out Dictionary<string, string> paramMap)
    {
        paramMap = config.SchemaFilter
            .Select((s, i) => (Key: $"@{paramPrefix}{i}", Value: s))
            .ToDictionary(x => x.Key, x => x.Value);
        return string.Join(", ", paramMap.Keys);
    }

    private static void AddSchemaParams(NpgsqlCommand cmd, Dictionary<string, string> paramMap)
    {
        foreach (var kv in paramMap)
            cmd.Parameters.AddWithValue(kv.Key, kv.Value);
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        var inClause = SchemaInClause("s", out var paramMap);
        var sql = $"""
            SELECT
                t.table_schema,
                t.table_name,
                t.table_type,
                obj_description(format('%I.%I', t.table_schema, t.table_name)::regclass, 'pg_class') AS description
            FROM information_schema.tables t
            WHERE t.table_schema IN ({inClause})
            ORDER BY t.table_schema, t.table_type, t.table_name
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        AddSchemaParams(cmd, paramMap);

        var results = new List<TableInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TableInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return results;
    }

    public async Task<TableSchemaResult> GetTableSchemaAsync(
        string tableName,
        string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        var schema = schemaName ?? config.SchemaFilter.FirstOrDefault() ?? "public";

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
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT table_type FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @table
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string ?? "BASE TABLE";
    }

    private static async Task<List<ColumnInfo>> GetColumnsAsync(
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT
                c.column_name,
                c.data_type,
                c.character_maximum_length,
                c.numeric_precision,
                c.numeric_scale,
                c.is_nullable,
                c.column_default,
                CASE WHEN c.identity_generation IS NOT NULL OR c.column_default LIKE 'nextval%' THEN true ELSE false END AS is_identity,
                col_description(format('%I.%I', c.table_schema, c.table_name)::regclass, c.ordinal_position) AS description
            FROM information_schema.columns c
            WHERE c.table_schema = @schema AND c.table_name = @table
            ORDER BY c.ordinal_position
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var results = new List<ColumnInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var dataType = reader.GetString(1);
            var fullType = BuildFullType(dataType,
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4));

            results.Add(new ColumnInfo(
                ColumnName: reader.GetString(0),
                DataType: dataType,
                FullDataType: fullType,
                IsNullable: reader.GetString(5) == "YES",
                DefaultValue: reader.IsDBNull(6) ? null : reader.GetString(6),
                IsIdentity: reader.GetBoolean(7),
                IsPrimaryKey: false,
                Description: reader.IsDBNull(8) ? null : reader.GetString(8)));
        }
        return results;
    }

    private static string BuildFullType(string dataType, int? maxLength, int? precision, int? scale)
    {
        if (maxLength.HasValue && maxLength.Value > 0)
            return $"{dataType}({maxLength.Value})";
        if (precision.HasValue && scale.HasValue && scale.Value > 0)
            return $"{dataType}({precision.Value},{scale.Value})";
        if (precision.HasValue && precision.Value > 0)
            return $"{dataType}({precision.Value})";
        return dataType;
    }

    private static async Task<List<string>> GetPrimaryKeyColumnsAsync(
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY'
                AND tc.table_schema = @schema
                AND tc.table_name = @table
            ORDER BY kcu.ordinal_position
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(reader.GetString(0));
        return results;
    }

    private static async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT
                tc.constraint_name,
                kcu.column_name,
                ccu.table_schema AS referenced_schema,
                ccu.table_name AS referenced_table,
                ccu.column_name AS referenced_column,
                rc.delete_rule,
                rc.update_rule
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            JOIN information_schema.referential_constraints rc
                ON tc.constraint_name = rc.constraint_name
                AND tc.table_schema = rc.constraint_schema
            JOIN information_schema.constraint_column_usage ccu
                ON rc.unique_constraint_name = ccu.constraint_name
                AND rc.unique_constraint_schema = ccu.constraint_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
                AND tc.table_schema = @schema
                AND tc.table_name = @table
            ORDER BY tc.constraint_name, kcu.ordinal_position
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var results = new List<ForeignKeyInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ForeignKeyInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)));
        }
        return results;
    }

    private static async Task<List<IndexInfo>> GetIndexesAsync(
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT
                i.relname AS index_name,
                am.amname AS index_type,
                ix.indisunique AS is_unique,
                ix.indisprimary AS is_primary_key,
                a.attname AS column_name
            FROM pg_class t
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_index ix ON t.oid = ix.indrelid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_am am ON i.relam = am.oid
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
            WHERE n.nspname = @schema AND t.relname = @table
            ORDER BY i.relname, a.attnum
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var indexGroups = new Dictionary<string, (string Type, bool IsUnique, bool IsPk, List<string> Cols)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            if (!indexGroups.ContainsKey(name))
                indexGroups[name] = (reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), []);
            indexGroups[name].Cols.Add(reader.GetString(4));
        }

        return indexGroups.Select(kv =>
            new IndexInfo(kv.Key, kv.Value.Type, kv.Value.IsUnique, kv.Value.IsPk, kv.Value.Cols)).ToList();
    }

    private static async Task<List<ConstraintInfo>> GetConstraintsAsync(
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT
                tc.constraint_name,
                tc.constraint_type,
                cc.check_clause
            FROM information_schema.table_constraints tc
            LEFT JOIN information_schema.check_constraints cc
                ON tc.constraint_name = cc.constraint_name
                AND tc.table_schema = cc.constraint_schema
            WHERE tc.table_schema = @schema AND tc.table_name = @table
                AND tc.constraint_type IN ('CHECK', 'UNIQUE')
            ORDER BY tc.constraint_type, tc.constraint_name
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", tableName);

        var results = new List<ConstraintInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ConstraintInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return results;
    }

    public async Task<IReadOnlyList<ProcedureInfo>> ListStoredProceduresAsync(CancellationToken cancellationToken = default)
    {
        var inClause = SchemaInClause("s", out var paramMap);
        var sql = $"""
            SELECT routine_schema, routine_name, created, last_altered
            FROM information_schema.routines
            WHERE routine_type = 'PROCEDURE'
                AND routine_schema IN ({inClause})
            ORDER BY routine_schema, routine_name
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        AddSchemaParams(cmd, paramMap);

        var results = new List<ProcedureInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ProcedureInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        }
        return results;
    }

    public async Task<ProcedureDefinition?> GetProcedureDefinitionAsync(
        string procedureName,
        string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        var schema = schemaName ?? config.SchemaFilter.FirstOrDefault() ?? "public";

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        const string paramSql = """
            SELECT
                p.parameter_name,
                p.data_type,
                p.parameter_mode = 'OUT' OR p.parameter_mode = 'INOUT' AS is_output
            FROM information_schema.parameters p
            JOIN information_schema.routines r
                ON r.specific_schema = p.specific_schema
                AND r.specific_name = p.specific_name
            WHERE r.specific_schema = @schema
                AND r.routine_name = @procedure
            ORDER BY p.ordinal_position
            """;

        await using var paramCmd = new NpgsqlCommand(paramSql, conn);
        paramCmd.Parameters.AddWithValue("@schema", schema);
        paramCmd.Parameters.AddWithValue("@procedure", procedureName);

        var parameters = new List<ProcedureParameter>();
        {
            await using var paramReader = await paramCmd.ExecuteReaderAsync(cancellationToken);
            while (await paramReader.ReadAsync(cancellationToken))
            {
                parameters.Add(new ProcedureParameter(
                    paramReader.IsDBNull(0) ? "" : paramReader.GetString(0),
                    paramReader.GetString(1),
                    paramReader.GetBoolean(2),
                    false));
            }
        } // reader disposed here so the second command can use the same connection

        const string defSql = """
            SELECT pg_get_functiondef(p.oid)
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = @schema AND p.proname = @procedure
            LIMIT 1
            """;

        await using var defCmd = new NpgsqlCommand(defSql, conn);
        defCmd.Parameters.AddWithValue("@schema", schema);
        defCmd.Parameters.AddWithValue("@procedure", procedureName);
        var definition = await defCmd.ExecuteScalarAsync(cancellationToken) as string;

        return new ProcedureDefinition(schema, procedureName, definition, parameters);
    }

    public async Task<IReadOnlyList<FunctionInfo>> ListFunctionsAsync(CancellationToken cancellationToken = default)
    {
        var inClause = SchemaInClause("s", out var paramMap);
        var sql = $"""
            SELECT routine_schema, routine_name, routine_type, data_type AS return_type, created, last_altered
            FROM information_schema.routines
            WHERE routine_type = 'FUNCTION'
                AND routine_schema IN ({inClause})
            ORDER BY routine_schema, routine_name
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        AddSchemaParams(cmd, paramMap);

        var results = new List<FunctionInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new FunctionInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5)));
        }
        return results;
    }

    public async Task<FunctionDefinition?> GetFunctionDefinitionAsync(
        string functionName,
        string? schemaName = null,
        CancellationToken cancellationToken = default)
    {
        var schema = schemaName ?? config.SchemaFilter.FirstOrDefault() ?? "public";

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        const string paramSql = """
            SELECT
                p.parameter_name,
                p.data_type,
                p.parameter_mode = 'OUT' OR p.parameter_mode = 'INOUT' AS is_output
            FROM information_schema.parameters p
            JOIN information_schema.routines r
                ON r.specific_schema = p.specific_schema
                AND r.specific_name = p.specific_name
            WHERE r.specific_schema = @schema
                AND r.routine_name = @function
            ORDER BY p.ordinal_position
            """;

        await using var paramCmd = new NpgsqlCommand(paramSql, conn);
        paramCmd.Parameters.AddWithValue("@schema", schema);
        paramCmd.Parameters.AddWithValue("@function", functionName);

        var parameters = new List<FunctionParameter>();
        {
            await using var paramReader = await paramCmd.ExecuteReaderAsync(cancellationToken);
            while (await paramReader.ReadAsync(cancellationToken))
            {
                parameters.Add(new FunctionParameter(
                    paramReader.IsDBNull(0) ? "" : paramReader.GetString(0),
                    paramReader.GetString(1),
                    paramReader.GetBoolean(2)));
            }
        } // reader disposed here so the second command can use the same connection

        const string defSql = """
            SELECT pg_get_functiondef(p.oid)
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = @schema AND p.proname = @function
            LIMIT 1
            """;

        await using var defCmd = new NpgsqlCommand(defSql, conn);
        defCmd.Parameters.AddWithValue("@schema", schema);
        defCmd.Parameters.AddWithValue("@function", functionName);
        var definition = await defCmd.ExecuteScalarAsync(cancellationToken) as string;

        return new FunctionDefinition(schema, functionName, definition, parameters);
    }

    public async Task<SearchResult> SearchSchemaAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var inClause = SchemaInClause("s", out var schemaParams);
        var likeKeyword = $"%{keyword}%";

        var sql = $"""
            SELECT 'TABLE' AS object_type, table_schema, table_name, NULL AS detail
            FROM information_schema.tables
            WHERE table_schema IN ({inClause}) AND table_name ILIKE @kw
            UNION ALL
            SELECT 'COLUMN', table_schema, table_name, column_name
            FROM information_schema.columns
            WHERE table_schema IN ({inClause}) AND (table_name ILIKE @kw OR column_name ILIKE @kw)
            UNION ALL
            SELECT routine_type, routine_schema, routine_name, NULL
            FROM information_schema.routines
            WHERE routine_schema IN ({inClause}) AND routine_name ILIKE @kw
            ORDER BY object_type, table_schema, table_name
            """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        AddSchemaParams(cmd, schemaParams);
        cmd.Parameters.AddWithValue("@kw", likeKeyword);

        var matches = new List<SearchMatch>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new SearchMatch(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return new SearchResult(keyword, matches);
    }
}
