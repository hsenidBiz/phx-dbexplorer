# Phx DB Explorer — Cross-Database MCP Server

A read-only **Model Context Protocol (MCP) server** that exposes database schema information to AI coding assistants such as GitHub Copilot. It gives Copilot a deep understanding of your database structure — tables, columns, relationships, stored procedures, and functions — without ever touching your data.

Supports **Microsoft SQL Server** and **PostgreSQL**. Runs as a local stdio process, making it compatible with VS Code, GitHub Copilot, and any MCP-capable client.

---

## What It Does

When connected, Copilot (or any MCP client) can call 7 read-only tools to understand your database:

| Tool | What it returns |
|---|---|
| `list_tables` | All tables and views in the configured schema(s) with descriptions |
| `get_table_schema` | Full detail for one table: columns, data types, nullability, primary key, foreign keys, indexes, constraints |
| `list_stored_procedures` | All stored procedures with name, schema, created/modified dates |
| `get_procedure_definition` | Parameters (name, type, direction) and full SQL source of a procedure |
| `list_functions` | All user-defined functions with metadata |
| `get_function_definition` | Parameters and full SQL source of a function |
| `search_schema` | Keyword search across all tables, columns, procedures, and functions |

> **No data is ever read.** All queries exclusively target metadata views (`information_schema`, `sys.*`, `pg_catalog`). No `SELECT` from user tables is performed.

---

## Security Notes

- **Schema-only access** — the server only queries metadata views. User table data is never read.
- **No credential logging** — the connection string is never written to logs or returned in any tool response.
- **Schema filtering** — `SCHEMA_FILTER` limits visibility to only the schemas you explicitly allow. Defaults to the most restrictive single-schema setting.
- **Read-only transport** — all MCP tools are marked `ReadOnly = true`, preventing write operations at the protocol level.
- **Least-privilege recommended** — grant the database user only `VIEW DEFINITION` and access to `information_schema`/`sys` views. No data permissions are needed.

### Recommended minimum database permissions

**SQL Server:**
```sql
GRANT VIEW DEFINITION TO [mcp_user];
GRANT SELECT ON SCHEMA::information_schema TO [mcp_user];
GRANT SELECT ON SCHEMA::sys TO [mcp_user];
```

**PostgreSQL:**
```sql
GRANT USAGE ON SCHEMA information_schema TO mcp_user;
GRANT SELECT ON ALL TABLES IN SCHEMA information_schema TO mcp_user;
GRANT SELECT ON ALL TABLES IN SCHEMA pg_catalog TO mcp_user;
```

---

---

# 👤 For Consumers

> You want to use this MCP server in your local development environment to give GitHub Copilot awareness of your database schema. You do not need to clone or build anything.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- VS Code with the [GitHub Copilot](https://marketplace.visualstudio.com/items?itemName=GitHub.copilot) extension
- Access to a Microsoft SQL Server or PostgreSQL database
- An Azure DevOps **Personal Access Token (PAT)** with `Packaging (Read)` scope
  - Generate at: Azure DevOps → User Settings → Personal Access Tokens → New Token
  - Select scope: **Packaging → Read**

---

## Step 1 — Register the Private NuGet Feed

Run this once per machine:

```bash
dotnet nuget add source https://pkgs.dev.azure.com/PeoplesHr/_packaging/phx-dbexplorer/nuget/v3/index.json \
  --name PeoplesHr \
  --username YOUR_AZURE_DEVOPS_USERNAME \
  --password YOUR_PAT_TOKEN \
  --store-password-in-clear-text
```

> The `--store-password-in-clear-text` flag is required on Windows. The PAT is stored in your local NuGet config (`~/.nuget/NuGet/NuGet.Config`) and is never shared.

---

## Step 2 — Install the Tool

```bash
dotnet tool install -g PhxDbExplorer --add-source PeoplesHr
```

Verify:
```bash
phx-dbexplorer --version
```

---

## Step 3 — Configure GitHub Copilot

You can configure the MCP server at the **global level** (available in every workspace) or at the **project level** (scoped to a specific repository). Choose the approach that best fits your workflow.

---

### Option A — Global Configuration (Copilot CLI)

Applies to all workspaces on your machine. Add the following to `~/.copilot/mcp-config.json` (create the file if it does not exist):

```json
{
  "mcpServers": {
    "phx-dbexplorer": {
      "type": "stdio",
      "command": "phx-dbexplorer",
      "args": [],
      "env": {
        "DB_TYPE": "mssql",
        "CONNECTION_STRING": "Server=your-server;Database=your-database;User Id=your-user;Password=your-password;TrustServerCertificate=True;",
        "SCHEMA_FILTER": "dbo"
      }
    }
  }
}
```

> **Never commit `mcp-config.json` to source control**— it contains your database credentials.

---

### Option B — Project-Level Configuration (Copilot CLI + Visual Studio)

Scopes the MCP server to a single repository and its specific database. Each project can have its own connection string and schema filter without touching the global config.

Create a `.mcp.json` file with the content below. Place it in **both** locations to cover all clients:

| Location | Client |
|---|---|
| Repository root (e.g. `MyProject\.mcp.json`) | Copilot CLI |
| Solution directory (folder containing your `.sln`/`.slnx`) | Visual Studio 2022 17.14.9+ |

> If your solution file sits in a subfolder (e.g. `src\`), you need a `.mcp.json` in **both** the repo root (for Copilot CLI) and that subfolder (for Visual Studio).

```json
{
  "mcpServers": {
    "phx-dbexplorer": {
      "type": "stdio",
      "command": "phx-dbexplorer",
      "args": [],
      "env": {
        "DB_TYPE": "mssql",
        "CONNECTION_STRING": "Server=your-server;Database=your-database;User Id=your-user;Password=your-password;TrustServerCertificate=True;",
        "SCHEMA_FILTER": "dbo"
      }
    }
  },
  "servers": {
    "phx-dbexplorer": {
      "type": "stdio",
      "command": "phx-dbexplorer",
      "args": [],
      "env": {
        "DB_TYPE": "mssql",
        "CONNECTION_STRING": "Server=your-server;Database=your-database;User Id=your-user;Password=your-password;TrustServerCertificate=True;",
        "SCHEMA_FILTER": "dbo"
      }
    }
  }
}
```

> **Why two keys?** Copilot CLI reads `"mcpServers"`. Visual Studio reads `"servers"`. Both must be present in the file so a single `.mcp.json` covers both clients.

#### Keeping credentials out of source control

Add `.mcp.json` to your `.gitignore` and commit a placeholder `.mcp.json.example` instead:

```
# .gitignore
.mcp.json
!.mcp.json.example
```

Each developer copies `.mcp.json.example` → `.mcp.json` and fills in their own connection string.

---

### Environment Variables

| Variable | Required | Description | Example |
|---|---|---|---|
| `DB_TYPE` | ✅ | Database engine: `mssql` or `postgres` | `mssql` |
| `CONNECTION_STRING` | ✅ | Full ADO.NET connection string | See examples below |
| `SCHEMA_FILTER` | ❌ | Comma-separated schemas to expose. Defaults to `dbo` (SQL Server) or `public` (PostgreSQL) | `dbo` or `hr,public` |

### Connection String Examples

**SQL Server — Windows (Integrated) Authentication:**
```
Server=localhost;Database=HRDatabase;Trusted_Connection=True;TrustServerCertificate=True;
```

**SQL Server — SQL Authentication:**
```
Server=localhost;Database=HRDatabase;User Id=sa;Password=YourPassword;TrustServerCertificate=True;
```

**PostgreSQL:**
```
Host=localhost;Port=5432;Database=hrdb;Username=postgres;Password=YourPassword;
```

Restart Copilot CLI or Visual Studio after saving the config file. The `phx-dbexplorer` server will appear as an available MCP server automatically.

---

## Getting Updates

When a new version is released, run:

```bash
dotnet tool update -g PhxDbExplorer
```

Then restart Copilot CLI or Visual Studio. **Your config file never needs to change.**

### Other useful commands

```bash
# Check your installed version
dotnet tool list -g | findstr PhxDbExplorer

# Pin to a specific version
dotnet tool update -g PhxDbExplorer --version 1.0.0
```

---

## Example Copilot Interactions

Once connected, you can ask Copilot questions like:

- *"What tables are in the PeoplsHR database?"*
- *"Show me the schema for the Employees table including all foreign keys."*
- *"What stored procedures exist? Show me the definition of `sp_GetEmployeeById`."*
- *"Search the schema for anything related to 'payroll'."*
- *"Write a C# repository class for the Orders table using the exact column names and types."*

Copilot will automatically call the appropriate MCP tools to retrieve the schema and use it to generate accurate, schema-aware code.

---

---

# 🛠️ For Contributors

> You are making changes to this project — adding features, fixing bugs, or publishing new releases.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker Desktop (required for integration tests)
- Access to the Azure DevOps repository and Azure Artifacts feed

---

## Project Structure

```
PhxDbExplorer/
├── PhxDbExplorer/              # Main MCP server
│   ├── Program.cs                      # Entry point — DI, MCP server, stdio transport
│   ├── Configuration/
│   │   └── DatabaseConfig.cs           # Reads and validates environment variables
│   ├── Providers/
│   │   ├── ISchemaProvider.cs          # Interface defining all schema query methods
│   │   ├── SqlServerSchemaProvider.cs  # SQL Server implementation
│   │   ├── PostgresSchemaProvider.cs   # PostgreSQL implementation
│   │   └── SchemaProviderFactory.cs    # Selects provider based on DB_TYPE
│   ├── Models/                         # Immutable record types for schema objects
│   │   ├── TableInfo.cs
│   │   ├── ColumnInfo.cs
│   │   ├── IndexInfo.cs
│   │   ├── ForeignKeyInfo.cs
│   │   ├── ConstraintInfo.cs
│   │   ├── ProcedureInfo.cs
│   │   └── FunctionInfo.cs
│   └── Tools/
│       └── SchemaTools.cs              # MCP tool registrations ([McpServerTool])
│
├── PhxDbExplorer.Tests/        # Unit tests (xUnit + Moq + FluentAssertions)
├── PhxDbExplorer.IntegrationTests/  # Integration tests (Testcontainers)
└── PhxDbExplorer.slnx          # Solution file
```

---

## Running Locally

```powershell
# Set environment variables (PowerShell)
$env:DB_TYPE = "mssql"
$env:CONNECTION_STRING = "Server=localhost;Database=HRDatabase;Trusted_Connection=True;TrustServerCertificate=True;"
$env:SCHEMA_FILTER = "dbo"

# Run
dotnet run --project PhxDbExplorer
```

The server starts on **stdio** and waits for MCP client connections. Startup confirmation is logged to stderr. If environment variables are missing or the database is unreachable, a descriptive error is printed and the process exits.

---

## Running Tests

### Unit Tests
```bash
dotnet test PhxDbExplorer.Tests
```
31 tests covering configuration validation, provider factory selection, and all 7 MCP tools with mocked providers.

### Integration Tests

Requires **Docker Desktop** to be running. Testcontainers automatically pulls and starts real database containers.

```bash
dotnet test PhxDbExplorer.IntegrationTests
```

This spins up SQL Server 2022 and PostgreSQL 16 containers, seeds a test schema (tables, views, stored procedures, functions), runs all provider tests against them, and shuts the containers down when done.

---

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| `ModelContextProtocol` | 1.2.0 | Official MCP SDK — stdio server, tool registration |
| `Microsoft.Data.SqlClient` | 7.0.1 | SQL Server connectivity |
| `Npgsql` | 10.0.2 | PostgreSQL connectivity |
| `Microsoft.Extensions.Hosting` | 10.0.7 | Dependency injection and host lifetime |
| `Microsoft.Extensions.Logging.Console` | 10.0.7 | Startup diagnostics (logged to stderr only) |

---

## Releasing a New Version

This project is distributed as a **.NET Global Tool** via **Azure Artifacts**. The release process is automated through Azure Pipelines — you only need to bump the version and push a tag.

### 1. Bump the version

Edit `PhxDbExplorer/PhxDbExplorer.csproj` and increment `<Version>` following [Semantic Versioning](https://semver.org):

| Change type | Example |
|---|---|
| Bug fix | `1.0.0` → `1.0.1` |
| New feature, backward compatible | `1.0.0` → `1.1.0` |
| Breaking change | `1.0.0` → `2.0.0` |

### 2. Commit, tag, and push

```bash
git add .
git commit -m "release: v1.1.0 - <short description>"
git tag v1.1.0
git push origin main --tags
```

### 3. Azure Pipelines publishes automatically

Pushing a tag triggers the pipeline which:
1. Runs all unit tests — **if tests fail, nothing is published**
2. Packs the project into a `.nupkg`
3. Pushes to the Azure Artifacts NuGet feed

Consumers can run `dotnet tool update -g PhxDbExplorer` as soon as the pipeline completes.

---

## Azure Pipelines Configuration Reference

`azure-pipelines.yml` in the repository root:

```yaml
trigger:
  tags:
    include:
      - v*

pool:
  vmImage: ubuntu-latest

variables:
  buildConfiguration: Release

steps:
  - task: UseDotNet@2
    displayName: Use .NET 10 SDK
    inputs:
      packageType: sdk
      version: '10.0.x'

  - task: DotNetCoreCLI@2
    displayName: Run unit tests
    inputs:
      command: test
      projects: PhxDbExplorer.Tests/PhxDbExplorer.Tests.csproj
      arguments: --configuration $(buildConfiguration)

  - task: DotNetCoreCLI@2
    displayName: Pack NuGet package
    inputs:
      command: pack
      packagesToPack: PhxDbExplorer/PhxDbExplorer.csproj
      configuration: $(buildConfiguration)
      outputDir: $(Build.ArtifactStagingDirectory)/nupkg

  - task: NuGetAuthenticate@1
    displayName: Authenticate to Azure Artifacts

  - task: DotNetCoreCLI@2
    displayName: Push to Azure Artifacts
    inputs:
      command: push
      packagesToPush: $(Build.ArtifactStagingDirectory)/nupkg/*.nupkg
      nuGetFeedType: internal
      publishVstsFeed: PeoplesHr/phx-dbexplorer
```

### dotnet tool packaging properties (already in `.csproj` — do not remove)

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>phx-dbexplorer</ToolCommandName>
<PackageId>PhxDbExplorer</PackageId>
<Version>1.0.0</Version>
<Description>Read-only MCP server exposing database schema to GitHub Copilot</Description>
<Authors>PeoplesHr</Authors>
```

> `ToolCommandName` is the command consumers type in their terminal. `PackageId` is the NuGet package identifier. These must not be changed without coordinating with all consumers.
