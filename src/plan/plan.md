# MCP Schema Server — Implementation Plan

## Problem Statement
Build a read-only MCP (Model Context Protocol) server on top of the existing .NET 10 console application `PhxDbExplorer`. The server exposes database schema information (no data) to GitHub Copilot via the official stdio transport, supporting both MS SQL Server and PostgreSQL.

## Decisions Made
| Concern | Decision |
|---|---|
| Transport | stdio (local use with Copilot / VS Code) |
| MCP SDK | Official `ModelContextProtocol` NuGet package |
| Database auth | SQL Auth + Windows/Integrated Auth (both supported) |
| Connection config | Environment variables (`DB_TYPE`, `CONNECTION_STRING`, `SCHEMA_FILTER`) |
| Multi-DB | One database at a time, configured at startup |
| Schema scope | Schema name filtering via `SCHEMA_FILTER` env var |
| Data access | Schema/metadata, plus guarded read-only row data (see *Data Read* below) |

## Environment Variables
| Variable | Description | Example |
|---|---|---|
| `DB_TYPE` | `mssql` or `postgres` | `mssql` |
| `CONNECTION_STRING` | Full ADO.NET connection string | `Server=.;Database=HR;Trusted_Connection=True;` |
| `SCHEMA_FILTER` | Comma-separated schema names to expose | `dbo` or `public,hr` |

## MCP Tools Exposed
| Tool | Description |
|---|---|
| `list_tables` | List all tables and views in the filtered schema(s) |
| `get_table_schema` | Full schema of a table/view: columns, types, nullability, PK, FK, indexes, constraints |
| `list_stored_procedures` | List all stored procedures |
| `get_procedure_definition` | Parameters and definition of a stored procedure |
| `list_functions` | List all user-defined functions |
| `get_function_definition` | Parameters and definition of a UDF |
| `search_schema` | Search tables/columns/procedures/functions by keyword |

## Project Structure
```
PhxDbExplorer/
├── Program.cs                          # Entry point: DI setup + MCP server + stdio transport
├── Configuration/
│   └── DatabaseConfig.cs               # Reads env vars, validates at startup
├── Providers/
│   ├── ISchemaProvider.cs              # Interface for all schema queries
│   ├── SqlServerSchemaProvider.cs      # MS SQL Server implementation (information_schema + sys)
│   ├── PostgresSchemaProvider.cs       # PostgreSQL implementation (information_schema + pg_catalog)
│   └── SchemaProviderFactory.cs        # Factory: picks provider from DB_TYPE
├── Models/
│   ├── TableInfo.cs                    # Table/view metadata
│   ├── ColumnInfo.cs                   # Column details (type, nullable, default, identity)
│   ├── IndexInfo.cs                    # Index metadata
│   ├── ForeignKeyInfo.cs               # FK relationships
│   ├── ConstraintInfo.cs               # Check + unique constraints
│   ├── ProcedureInfo.cs                # Stored procedure metadata + parameters
│   └── FunctionInfo.cs                 # UDF metadata + parameters
└── Tools/
    └── SchemaTools.cs                  # MCP [McpServerTool] method registrations
```

## NuGet Packages to Add
| Package | Purpose |
|---|---|
| `ModelContextProtocol` | Official MCP SDK (stdio server, tool registration) |
| `Microsoft.Data.SqlClient` | MS SQL Server connectivity |
| `Npgsql` | PostgreSQL connectivity |
| `Microsoft.Extensions.Hosting` | DI, configuration, hosted service lifetime |
| `Microsoft.Extensions.Logging.Console` | Startup/diagnostic logging (stderr only) |

## Security Constraints
- Connection is opened with the exact connection string provided — no privilege escalation
- All queries target `information_schema`, `sys.*` (SQL Server), and `pg_catalog` / `information_schema` (Postgres) — metadata views only
- Row-data reads go through `sample_table_data` / `execute_query` only, and are held to
  read-only by four independent layers: statement validation, an always-rolled-back
  transaction (`READ ONLY` on PostgreSQL), catalog-resolved identifiers that are never
  interpolated from caller input, and the `SCHEMA_FILTER` / `MAX_ROWS` limits
- `SCHEMA_FILTER` defaults to `dbo` (SQL Server) / `public` (Postgres) if not set — opt-in to expand
- Connection string never logged or exposed in tool responses

## Implementation Todos
1. **Add NuGet packages** to `.csproj`
2. **DatabaseConfig** — read + validate env vars
3. **Models** — plain record types for schema objects
4. **ISchemaProvider** — define interface contract
5. **SqlServerSchemaProvider** — implement all interface methods using `information_schema` + `sys`
6. **PostgresSchemaProvider** — implement all interface methods using `information_schema` + `pg_catalog`
7. **SchemaProviderFactory** — create correct provider from `DB_TYPE`
8. **SchemaTools** — register MCP tools using `[McpServerTool]` attributes
9. **Program.cs** — wire DI, register MCP server with stdio transport
10. **Validation** — build and verify startup error handling

## Sample MCP Config (VS Code / Copilot)
```json
{
  "servers": {
    "phx-dbexplorer": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "PhxDbExplorer"],
      "env": {
        "DB_TYPE": "mssql",
        "CONNECTION_STRING": "Server=localhost;Database=YourDb;Trusted_Connection=True;TrustServerCertificate=True;",
        "SCHEMA_FILTER": "dbo"
      }
    }
  }
}
```

---

## Data Read (added after the original schema-only scope)

The original plan deliberately executed no row-data queries. That constraint was lifted:
assistants that can see a schema but not a single row cannot tell a nullable column from an
always-null one, or a lookup table from a dead one.

| Tool | Purpose |
|---|---|
| `sample_table_data` | Rows from one table/view, with optional WHERE, ORDER BY, and limit |
| `execute_query` | A single read-only SELECT / WITH … SELECT, for joins and aggregates |
| `get_table_row_count` | Exact row count for a table or view |
| `get_data_read_limits` | The active MAX_ROWS, query timeout, and readable schemas |

| Concern | Decision |
|---|---|
| Gating | Always on — no enable flag; the connection string's own grants are the access boundary |
| Row cap | `MAX_ROWS` (default 100), applied while reading, so arbitrary SQL is never rewritten |
| Timeout | `QUERY_TIMEOUT_SECONDS` (default 30) |
| Write prevention | Validator + always-rolled-back transaction (`READ ONLY` on PostgreSQL) |
| Identifiers | Resolved against the catalog; only catalog spellings reach the SQL text |
| WHERE / ORDER BY | Raw fragments by necessity — validated, and `;`/unbalanced parens rejected |

New files: `Query/ReadOnlySqlValidator.cs`, `Providers/IDataProvider.cs`,
`Providers/QueryResultReader.cs`, `Providers/{SqlServer,Postgres}DataProvider.cs`,
`Models/QueryResult.cs`, `Tools/DataTools.cs`.
