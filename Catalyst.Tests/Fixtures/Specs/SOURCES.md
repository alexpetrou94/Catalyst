# Spec fixtures

Real-world specs are copied verbatim (museum was converted from YAML to JSON). All were retrieved on 2026-10-19.

| File | Origin | Licence |
| --- | --- | --- |
| petstore.json | https://petstore3.swagger.io/api/v3/openapi.json | Apache-2.0 |
| eshop-catalog.json | https://github.com/dotnet/eShop/blob/main/src/Catalog.API/Catalog.API.json | MIT |
| spotify.json | https://api.apis.guru/v2/specs/spotify.com/1.0.0/openapi.json (from github.com/sonallux/spotify-web-api) | MIT |
| museum.json | https://github.com/Redocly/museum-openapi-example (openapi.yaml converted to JSON) | MIT |

The other specs are written for this repository:

| File | Purpose |
| --- | --- |
| enterprise.json | Fastify-style API (paths with `{*}`, inline request and response objects) |
| minimal.json | Small spec covering parameters, bodies and primitive returns |
| edge-cases.json | Synthetic spec with one case per generator rule that the real specs do not exercise |
