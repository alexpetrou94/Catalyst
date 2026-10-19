namespace Catalyst.Common.Models;

/// <summary>A machine-readable problem or warning: a stable <see cref="IssueCodes"/> value plus the human text.</summary>
internal sealed record Issue(string Code, string Message);

/// <summary>
/// The stable codes reported in the --json output. Clients may match on these, so they never change
/// meaning; new problems get new codes.
/// </summary>
internal static class IssueCodes
{
    // Errors
    public const string SourceNotFound = "SOURCE_NOT_FOUND";
    public const string SourceFetchFailed = "SOURCE_FETCH_FAILED";
    public const string SourceTimeout = "SOURCE_TIMEOUT";
    public const string ParseFailed = "PARSE_FAILED";
    public const string SpecInvalid = "SPEC_INVALID";
    public const string ConfigInvalid = "CONFIG_INVALID";
    public const string ConfigNotFound = "CONFIG_NOT_FOUND";
    public const string NoClients = "NO_CLIENTS";
    public const string ClientInvalid = "CLIENT_INVALID";
    public const string UnsupportedLanguage = "UNSUPPORTED_LANGUAGE";
    public const string WriteFailed = "WRITE_FAILED";
    public const string GenerationFailed = "GENERATION_FAILED";
    public const string Cancelled = "CANCELLED";
    public const string Usage = "USAGE";
    public const string OperationNotFound = "OPERATION_NOT_FOUND";
    public const string OperationAmbiguous = "OPERATION_AMBIGUOUS";
    public const string SchemaNotFound = "SCHEMA_NOT_FOUND";
    public const string BaseNotFound = "BASE_NOT_FOUND";

    // Warnings
    public const string SpecValidationErrors = "SPEC_VALIDATION_ERRORS";
    public const string DuplicateMethodName = "DUPLICATE_METHOD_NAME";
    public const string PathParameterMismatch = "PATH_PARAMETER_MISMATCH";
    public const string SkippedPaths = "SKIPPED_PATHS";
}
