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
| `--check` | Compare the generated code with the files on disk without writing; exit code 1 when a client is out of date or missing (see below) |
| `--json` | Print one JSON document on stdout instead of text (see below) |
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

## Keeping clients in sync (CI)

`--check` generates in memory and compares with the file on disk. It never writes anything (not even
the output folder). Without `--source` it checks every configured client:

```bash
catalyst gen openapi --check
catalyst gen openapi --source ./swagger.json --output ./src/api.ts --check
```

Each client prints `Up to date: <path>`, `Out of date: <path>` or `Missing: <path>` on stdout, followed
by a summary line. The exit code is `0` only when every client is up to date, otherwise `1`. `--check`
needs something to compare with, so `--source` without `--output` is a usage error.

A GitHub Actions step that fails when a committed client no longer matches the backend spec:

```yaml
- name: Check generated API clients are current
  run: catalyst gen openapi --check
```

## Machine-readable output (`--json`)

`--json` works in every mode (validate, generate, `--check`, single client or configured). stdout then
carries exactly **one JSON document** and nothing else; stderr stays empty except for unexpected
crashes. Fields are camelCase and in the order shown.

```json
{
  "schemaVersion": 1,
  "command": "gen openapi",
  "mode": "generate",
  "success": true,
  "exitCode": 0,
  "results": [
    {
      "name": null,
      "source": "pets.json",
      "outputPath": "/work/api/pets.ts",
      "status": "written",
      "warnings": [],
      "error": null
    }
  ],
  "error": null
}
```

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Always `1`; bumped on breaking changes to this document |
| `mode` | `validate` (`--source` only), `generate`, or `check` |
| `success`, `exitCode` | Overall result, and the process exit code |
| `results` | One entry per client: the single `--source` client (`name` is `null`) or each configured client. Empty in validate mode |
| `results[].outputPath` | Full, normalised path of the generated file; `null` when the run failed before it was known |
| `results[].status` | `written`, `unchanged` (generate); `upToDate`, `stale`, `missing` (check); `failed` (any mode) |
| `results[].warnings` | `{ "code", "message" }` entries |
| `results[].error` | `{ "code", "message" }` when `status` is `failed`, otherwise `null` |
| `spec` | Validate mode only: `title`, `version`, `openapi` (major.minor), `pathCount`, `schemaCount`, `operations` (`method`, `path`, `operationId`, `summary`) |
| `diagnostics` | Validate mode only: `{ "severity": "error" or "warning", "pointer", "message" }` from the OpenAPI reader |
| `error` | Top-level failure: no client could even be attempted (source unreadable in validate mode, config problems, usage, cancelled), or a validate run with spec errors (`SPEC_INVALID`) |

Validate mode, for `catalyst gen openapi --source pets.json --json`:

```json
{
  "schemaVersion": 1,
  "command": "gen openapi",
  "mode": "validate",
  "success": true,
  "exitCode": 0,
  "results": [],
  "spec": {
    "title": "Pets",
    "version": "1.0.0",
    "openapi": "3.0",
    "pathCount": 1,
    "schemaCount": 0,
    "operations": [
      { "method": "GET", "path": "/pets", "operationId": "listPets", "summary": "List pets" }
    ]
  },
  "diagnostics": [],
  "error": null
}
```

Check mode, for a client whose file is out of date (`catalyst gen openapi --source pets.json --output api/pets.ts --check --json`):

```json
{
  "schemaVersion": 1,
  "command": "gen openapi",
  "mode": "check",
  "success": false,
  "exitCode": 1,
  "results": [
    {
      "name": null,
      "source": "pets.json",
      "outputPath": "/work/api/pets.ts",
      "status": "stale",
      "warnings": [],
      "error": null
    }
  ],
  "error": null
}
```

Configured generate (`catalyst gen openapi --json`) has the same shape as the single-client example, with one
result per configured client, each with its `name`, and a failed client carrying its own `error` while the others
still run:

```json
{
  "schemaVersion": 1,
  "command": "gen openapi",
  "mode": "generate",
  "success": false,
  "exitCode": 1,
  "results": [
    { "name": "Pets", "source": "/work/pets.json", "outputPath": "/work/api/Pets.ts", "status": "written", "warnings": [], "error": null },
    {
      "name": "Orders",
      "source": "",
      "outputPath": null,
      "status": "failed",
      "warnings": [],
      "error": { "code": "CLIENT_INVALID", "message": "No source is configured for this client. Add a source file path or URL." }
    }
  ],
  "error": null
}
```

### Error and warning codes

These codes are stable; new problems get new codes, existing ones keep their meaning.

| Error code | When |
| --- | --- |
| `SOURCE_NOT_FOUND` | The spec file does not exist |
| `SOURCE_FETCH_FAILED` | The spec could not be read or downloaded |
| `SOURCE_TIMEOUT` | The download timed out |
| `PARSE_FAILED` | The content is not a readable OpenAPI document |
| `SPEC_INVALID` | Validate mode: the spec has validation errors (see `diagnostics`) |
| `CONFIG_INVALID` / `CONFIG_NOT_FOUND` / `NO_CLIENTS` | `catalyst.config.json` is unreadable, missing, or defines no clients |
| `CLIENT_INVALID` | A configured client lacks a name, an output or a source |
| `UNSUPPORTED_LANGUAGE` | `--language` is not `typescript` |
| `WRITE_FAILED` | The output file could not be written |
| `GENERATION_FAILED` | Unexpected failure while generating |
| `CANCELLED` | Stopped with Ctrl+C |
| `USAGE` | Invalid usage: an unknown option, a stray argument, a missing option value, or a combination of options that does not make sense. With `--json` it is reported as this document (detected by `--json` appearing in the arguments); otherwise as a message on stderr |

| Warning code | When |
| --- | --- |
| `SPEC_VALIDATION_ERRORS` | The spec has validation errors; generated code may be incomplete |
| `DUPLICATE_METHOD_NAME` | Two operations got the same method name and one was renamed |
| `PATH_PARAMETER_MISMATCH` | A path parameter is used in the URL but not declared, or declared but unused |
| `SKIPPED_PATHS` | Paths were skipped by the configured prefixes (the message holds the count) |

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success (with `--check`: every client is up to date) |
| `1` | A client failed, the spec is invalid (validate mode), or with `--check` a client is out of date or missing |
| `130` | Cancelled with Ctrl+C |
| `2` | Usage error: unknown option, stray argument, missing value, or options that do not go together (nothing is generated) |

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
