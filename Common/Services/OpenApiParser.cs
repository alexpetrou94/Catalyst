using Catalyst.Common.Models;
using Microsoft.OpenApi.Reader;
using UPhoricLibrary.Common;

namespace Catalyst.Common.Services;

internal static class OpenApiParser
{
    public static Result<OpenApiParseResult> Parse(string content)
    {
        try
        {
            var result = OpenApiModelFactory.Parse(content, "json", new OpenApiReaderSettings());

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
