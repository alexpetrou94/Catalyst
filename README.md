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

## Exploring an API (for humans and AI agents)

`catalyst api` answers "what does this endpoint take and return, and how do I call it with the generated
client?" in a few lines, using exactly the names and types of the generated client, without opening a
multi-megabyte spec or the whole generated file. **Agents should prefer `catalyst api show` over reading the
spec or the generated file.**

Every `api` command picks its spec the same way:

* `--source <path|url>` (with `--insecure` for self-signed HTTPS), or
* `--client <name>`: a client from `catalyst.config.json`; its source, skip paths and name are used, so the
  output matches the client you generated, or
* neither, when the config defines exactly one client. With several clients and no choice it is a usage
  error (exit code `2`) that lists the client names.

Skip paths are respected, and `--json` prints one JSON document with the same envelope as `gen openapi`.

```bash
catalyst api list --filter pets
catalyst api show "POST /pets"
catalyst api schema Pet --depth 0
```

`api list [--filter <text>] [--tag <tag>] [--method <verb>]` prints one line per operation, sorted by path
then method. `--filter` words must all match the path, operationId, method name, summary or tags
(case-insensitive):

```text
GET   /pets       client.get.ListPets   - List pets
POST  /pets       client.post.CreatePet - Create a pet
GET   /pets/{id}  client.get.pet        - Get a pet
```

`api show <operation> [--no-types]` takes `"GET /pets/{id}"` (the path may use other parameter names), an
operationId, or a generated call such as `get.pet` / `client.get.pet`. It prints the exact call signature, a
parameter table (name in the call, name on the wire, location, required, type, description), the body and
response types with their media types, and the TypeScript declarations of every type the operation uses, each
once, dependencies first:

```text
POST /pets
Create a pet

Call:
  client.post.CreatePet(body: NewPet, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<Pet>>

Body: NewPet (application/json, required)
Response: Pet (application/json)

Types:
export interface NewPet {
    name: string;
}

export type Pet = NewPet & { id: string };
```

`api schema <Name> [--depth <n>]` prints one generated type, by its TypeScript name or its original component
name, and the types it references (`--depth 0` is the type alone). `--no-types` and `--depth` keep the output
small.

An unknown operation or type exits with `1` and the code `OPERATION_NOT_FOUND` or `SCHEMA_NOT_FOUND`, with up
to five close suggestions; a selector that matches several operations gives `OPERATION_AMBIGUOUS` and
lists them.

### JSON output

Envelope: `schemaVersion` (`1`), `command` (`api list`, `api show` or `api schema`), `success`, `exitCode`,
the payload below, optional `suggestions` (not found or ambiguous), and `error` (`{ "code", "message" }` or
`null`).

| Command | Payload |
| --- | --- |
| `api list` | `operations`: `{ method, path, operationId, summary, deprecated, tags, call }` |
| `api show` | `operation`: `{ method, path, operationId, summary, description, tags, deprecated, verb, methodName, signature, parameters: [{ name, wireName, in, required, type, description }], body: { type, mediaType, required } or null, response: { type, mediaType }, referencedTypes, call }`, and `types` (omitted with `--no-types`) |
| `api schema` | `types` |

`types` entries are `{ name, kind (interface, type or enum), componentName, declaration, dependsOn }`, in
dependency order. The extra error codes are `OPERATION_NOT_FOUND`, `OPERATION_AMBIGUOUS` and
`SCHEMA_NOT_FOUND`; source and config problems use the codes of `gen openapi`.

### Telling an AI agent about it

Paste this into your repository's `CLAUDE.md` or `AGENTS.md`:

```markdown
## API client

The API client in `<path to generated client>` is generated by Catalyst from the backend's OpenAPI spec. Do not
edit it by hand and do not read the spec or the whole generated file to find an endpoint.

- Find an endpoint: `catalyst api list --filter <words>` (add `--client <name>` if several clients are configured).
- See how to call it, with parameters, body, response and the TypeScript types: `catalyst api show "GET /items/{id}"`
  (or an operationId, or `client.get.itemsId`). Add `--json` for machine-readable output.
- Look up one type: `catalyst api schema <Name>`.
- After the backend API changes, regenerate with `catalyst gen openapi`. To verify the client is current without
  writing anything, run `catalyst gen openapi --check` (exit code 1 means out of date).
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
