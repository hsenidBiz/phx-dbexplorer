# Phx DB Explorer MCP Server

A [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server that exposes your **SQL Server** database schema to AI coding assistants (GitHub Copilot, Cursor, etc.). It allows AI tools to discover tables, views, stored procedures, functions, indexes, foreign keys, and more — without writing any SQL themselves.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 or later | Required to build and run |
| [Docker](https://www.docker.com/products/docker-desktop) | Any recent version | **Required for integration tests only** |

---

## Project Structure

```
src/
├── PhxDbExplorer/                  # MCP server application
├── PhxDbExplorer.Tests/            # Unit tests (xUnit, Moq)
└── PhxDbExplorer.IntegrationTests/ # Integration tests (Testcontainers, requires Docker)
```

---

## Configuration

The server is configured entirely through environment variables.

| Variable | Required | Description |
|---|---|---|
| `DB_TYPE` | ✅ Yes | Database type. Use `mssql` or `sqlserver` for SQL Server. |
| `CONNECTION_STRING` | ✅ Yes | Full ADO.NET connection string for the target database. |
| `SCHEMA_FILTER` | ❌ No | Comma-separated list of schemas to expose (default: `dbo`). |

**Example values:**

```
DB_TYPE=mssql
CONNECTION_STRING=Server=localhost,1433;Database=MyDb;User Id=sa;Password=YourPassword;TrustServerCertificate=True;
SCHEMA_FILTER=dbo,hr
```

---

## Build

```bash
dotnet build
```

---

## Registering the Server with an MCP Client

The server is not launched directly. Instead, it is registered in your editor's MCP configuration file so the editor starts and manages it automatically.

### VS Code — `.vscode/mcp.json`

Create (or update) `.vscode/mcp.json` in your workspace:

```json
{
  "servers": {
    "phx-dbexplorer": {
      "type": "stdio",
      "command": "Path to PhxDbExplorer.exe",
      "args": [],
      "env": {
        "DB_TYPE": "mssql",
        "CONNECTION_STRING": "Server=localhost,1433;Database=YourDatabase;User Id=YourUsername;Password=YourPassword;TrustServerCertificate=True;",
        "SCHEMA_FILTER": "YourSchema"
      }
    }
  }
}
```

> **Tip:** For a published/built binary, replace the `dotnet run` command with the path to the compiled executable (e.g. `"command": "path/to/PhxDbExplorer.exe"`).

Once registered, restart your editor and the MCP server will be available to any AI assistant that supports the MCP protocol.

---

## Available MCP Tools

These tools are automatically available to your AI assistant once the server is running.

| Tool | Description |
|---|---|
| `list_tables` | Lists all tables and views in the configured schema(s) with type and description. |
| `get_table_schema` | Returns full schema for a table/view: columns, foreign keys, indexes, and constraints. |
| `list_stored_procedures` | Lists all stored procedures in the configured schema(s). |
| `get_procedure_definition` | Returns the full definition of a stored procedure including parameters and SQL source. |
| `list_functions` | Lists all user-defined functions (UDFs) in the configured schema(s). |
| `get_function_definition` | Returns the full definition of a function including parameters and SQL source. |
| `search_schema` | Case-insensitive keyword search across tables, views, columns, procedures, and functions. |

---

## Running Tests

### Unit Tests

No extra dependencies required.

```bash
dotnet test src/PhxDbExplorer.Tests
```

Tests use xUnit, Moq, and FluentAssertions to verify tool behavior and configuration logic in isolation.

### Integration Tests

> ⚠️ **Docker is required.** Integration tests use [Testcontainers](https://dotnet.testcontainers.org/) to automatically pull and start a **SQL Server 2022** container. Docker must be running before executing these tests.

```bash
dotnet test src/PhxDbExplorer.IntegrationTests
```

The container is started automatically at the beginning of the test run and torn down when the tests complete. No manual database setup is needed.

---

## Contributing

1. Fork the repository and create a feature branch.
2. Make your changes — keep them focused and well-tested.
3. Ensure all unit tests pass (`dotnet test src/PhxDbExplorer.Tests`).
4. Open a pull request with a clear description of the change.
