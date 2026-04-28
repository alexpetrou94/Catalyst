using Catalyst.Common.Models;
using Microsoft.OpenApi.Readers;
using UPhoricLibrary.Common;

namespace Catalyst.Common.Services;

internal static class OpenApiParser
{
    public static Result<OpenApiParseResult> Parse(string content)
    {
        try
        {
            var reader = new OpenApiStringReader();
            var document = reader.Read(content, out var diagnostic);

            return Result<OpenApiParseResult>.Ok(new OpenApiParseResult
            {
                Document = document,
                Diagnostic = diagnostic,
            });
        }
        catch (Exception ex)
        {
            return Result<OpenApiParseResult>.Error($"Failed to parse OpenAPI document: {ex.Message}");
        }
    }
}
