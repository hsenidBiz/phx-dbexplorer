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
| Data access | Schema/metadata only — zero row-data queries |

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
- No `SELECT * FROM <user_table>` queries ever executed
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
