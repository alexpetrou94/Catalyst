# Catalyst

A small .NET CLI toolkit for automating code generation tasks. Today it turns OpenAPI
specifications into dependency-free TypeScript clients.

## Install

As a .NET tool from the GitHub Packages feed (`catalyst-cli`):

```bash
dotnet tool install --global catalyst-cli --add-source https://nuget.pkg.github.com/alexpetrou94/index.json
```

Or download a standalone binary from the [latest release](../../releases/latest)
(`catalyst-windows-amd64.exe`, `catalyst-linux-amd64`, `catalyst-darwin-amd64`, `catalyst-darwin-arm64`).

## Usage

Validate and inspect a specification:

```bash
catalyst gen openapi --source ./swagger.json
catalyst gen openapi --source https://localhost:7036/openapi/v1.json --insecure
```

Generate a TypeScript client:

```bash
catalyst gen openapi \
  --source https://localhost:7036/openapi/v1.json \
  --language typescript \
  --output ./src/external-apis \
  --class-name SyncfullyCommunicationApi \
  --insecure
```

| Option | Description |
| --- | --- |
| `--source <path\|url>` | Local JSON file or URL to an OpenAPI specification (required) |
| `--language <language>` | Output language; `typescript` is supported |
| `--output <path>` | Output file, or a directory (the file is named after `--class-name`) |
| `--class-name <name>` | Generated client name; defaults to the API title |
| `--skip-paths <paths>` | Comma-separated path prefixes to skip; overrides `catalyst.config.json` |
| `--insecure` | Skip TLS validation for a local HTTPS server with a self-signed certificate |
| `--version` | Print the tool version |

Settings can also live in a `catalyst.config.json` (or `.catalyst.config.json`) found by
walking up from the current directory:

```json
{ "gen": { "openapi": { "skipPaths": ["/api/auth/", "/internal/"] } } }
```

## Releases

Pushing a tag such as `v0.3.0` runs CI and publishes:

- the `catalyst-cli` NuGet package to GitHub Packages, and
- standalone binaries attached to the GitHub release, with `.sha256` checksums.

The [Syncfully Workbench](https://github.com/Syncfully/syncfully-db-workbench) downloads the
matching binary from `releases/latest/download` and verifies its checksum and `--version`.

## Development

```bash
dotnet build
dotnet test
```
