using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Catalyst.Common.Models;

internal sealed class OpenApiParseResult
{
    public required OpenApiDocument Document { get; init; }
    public required OpenApiDiagnostic Diagnostic { get; init; }
}
