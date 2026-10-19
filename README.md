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

Generate a TypeScript client (`--output` alone is enough; the language defaults to `typescript`):

```bash
catalyst gen openapi \
  --source https://localhost:7036/openapi/v1.json \
  --output ./src/external-apis \
  --class-name SyncfullyCommunicationApi \
  --insecure
```

Specifications can be JSON or YAML, from a file or a URL. The format is detected from the
extension (`.json`, `.yaml`, `.yml`) and otherwise from the content.

Generate **every** client configured in `catalyst.config.json` (see below) in one run:

```bash
catalyst gen openapi
```

| Option | Description |
| --- | --- |
| `--source <path\|url>` | Local JSON or YAML file, or URL, of an OpenAPI specification. Omit it to generate every configured client |
| `--language <language>` | Output language; `typescript` is supported and is the default whenever `--output` is given. With neither option the spec is only validated |
| `--output <path>` | Output file, or a folder (the file is named after `--class-name`). A path with a trailing separator, an existing directory, or no file extension is a folder |
| `--class-name <name>` | Generated client name; defaults to the API title |
| `--skip-paths <paths>` | Comma-separated path prefixes to skip; overrides `catalyst.config.json` |
| `--insecure` | Skip TLS validation for a local HTTPS server with a self-signed certificate |
| `--version` | Print the tool version |

Settings can also live in a `catalyst.config.json` (or `.catalyst.config.json`) found by
walking up from the current directory:

```json
{ "gen": { "openapi": { "skipPaths": ["/api/auth/", "/internal/"] } } }
```

### Client definitions

The optional `gen.openapi.clients` list describes the clients a repository consumes. Running
`catalyst gen openapi` without `--source` generates **every** configured client in one run, and
tools such as the Syncfully Workbench list these projects and prefill their generator form:

```json
{
  "gen": {
    "openapi": {
      "skipPaths": ["/internal/"],
      "clients": [
        {
          "name": "SyncfullyCommunicationApi",
          "source": "https://localhost:7036/openapi/v1.json",
          "output": "src/external-apis",
          "insecure": true
        }
      ]
    }
  }
}
```

| Client field | Meaning                                                       |
| ------------ | ------------------------------------------------------------- |
| `name`       | Generated client name (`--class-name`)                        |
| `output`     | Output folder or file, **relative to the config file's folder** |
| `source`     | OpenAPI file path or URL; relative paths resolve against the config file's folder |
| `insecure`   | Optional; mirror of the `--insecure` flag                      |
| `skipPaths`  | Optional per-client skip paths; falls back to `gen.openapi.skipPaths` |

Notes:

* `output` follows the same rule as `--output`: a path with a trailing separator, an existing
  directory, or no file extension is a folder and the file is named after `name`; use a `.ts`
  file path to control the exact file name.
* `catalyst gen openapi` exits `0` only when every configured client was written; clients that
  cannot be generated (for example a missing `source`) are reported and the exit code is `1`.
* `--source` and the other single-client options stay available and unchanged for one-off runs.
* Any failure while generating one client (not just a missing source) fails only that client; the
  others still run.

### Output behaviour

* The generated file is deterministic: it carries the generator version but no timestamp, so
  regenerating an unchanged spec yields an identical file.
* A file whose content would not change is not rewritten (its modification time stays put). The
  run still prints `TypeScript generated: <path>` (tools parse that line), followed by
  `Unchanged since the last generation; file not rewritten.`
* Errors and warnings are written to **stderr**; results and progress go to **stdout**. Ctrl+C
  stops the run with `Cancelled.` and exit code `130`.
* Request and response bodies follow the media types of the spec: JSON, `multipart/form-data`,
  `application/x-www-form-urlencoded`, `text/plain` and raw binary bodies are encoded by the
  generated client, and binary responses (images, PDFs, octet-stream) are returned as `Blob`.

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
