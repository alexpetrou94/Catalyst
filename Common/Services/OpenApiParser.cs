using Catalyst.Common.Models;
using Microsoft.OpenApi.Reader;
using Auxil.Common;

namespace Catalyst.Common.Services;

internal static class OpenApiParser
{
    public static Result<OpenApiParseResult> Parse(string content)
    {
        try
        {
            ReadResult result = OpenApiModelFactory.Parse(content, "json", new OpenApiReaderSettings());

            if (result.Document is null || result.Diagnostic is null)
            {
                return Result<OpenApiParseResult>.Error("Failed to parse OpenAPI document: produced an empty result.");
            }

            return Result<OpenApiParseResult>.Ok(new OpenApiParseResult
            {
                Document = result.Document,
                Diagnostic = result.Diagnostic,
            });
        }
        catch (Exception ex)
        {
            return Result<OpenApiParseResult>.Error($"Failed to parse OpenAPI document: {ex.Message}");
        }
    }
}
