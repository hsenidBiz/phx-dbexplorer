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

## Installation

### Option A — Download a prebuilt binary (recommended)

Self-contained, single-file executables are published as [GitHub Releases](../../releases) for every tagged version — no .NET SDK (or even the .NET runtime) required on the target machine.

1. Go to the [Releases](../../releases) page.
2. Download the archive matching your OS/architecture:

   | Asset | Platform |
   |---|---|
   | `PhxDbExplorer-<version>-win-x64.zip` | Windows x64 |
   | `PhxDbExplorer-<version>-linux-x64.tar.gz` | Linux x64 |
   | `PhxDbExplorer-<version>-osx-x64.tar.gz` | macOS (Intel) |
   | `PhxDbExplorer-<version>-osx-arm64.tar.gz` | macOS (Apple Silicon) |

3. Extract it and point your MCP client at the extracted `PhxDbExplorer` (or `PhxDbExplorer.exe`) binary.

Each release should have all four assets attached — if one is missing, check the [`release` workflow run](../../actions/workflows/release.yml) for that tag.

### Option B — Build from source

Requires the .NET SDK (see Prerequisites above).

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

## Releasing

Pushing a tag matching `v*.*.*` (e.g. `v1.2.0`) triggers the [`release` workflow](.github/workflows/release.yml), which runs the unit tests, publishes self-contained single-file binaries for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`, and attaches them to a new GitHub Release.

```bash
git tag v1.2.0
git push origin v1.2.0
```

See [`docs/readme.md`](docs/readme.md) for the full CI/CD pipeline documentation, including artifact naming, job breakdown, and pipeline verification history.

---

## Contributing

1. Fork the repository and create a feature branch.
2. Make your changes — keep them focused and well-tested.
3. Ensure all unit tests pass (`dotnet test src/PhxDbExplorer.Tests`).
4. Open a pull request with a clear description of the change.

Every pull request runs the [`CI` workflow](.github/workflows/ci.yml) automatically: build, unit tests, and integration tests (Testcontainers spins up its own SQL Server container on the runner — no setup needed on your end).
