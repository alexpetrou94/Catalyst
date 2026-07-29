using System.Reflection;

namespace Catalyst.Tests.Services;

public class IdentifierHelperTests
{
    private static string Invoke(string methodName, string value)
    {
        MethodInfo? method = typeof(TypeScriptGenerator).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            [typeof(string)],
            null);

        if (method is null)
        {
            throw new InvalidOperationException($"Method {methodName} not found.");
        }

        return (string)method.Invoke(null, [value])!;
    }

    [Theory]
    [InlineData("userId", "userId")]
    [InlineData("user-id", "user_id")]
    [InlineData("123abc", "_123abc")]
    [InlineData("class", "\"class\"")]
    [InlineData("interface", "\"interface\"")]
    [InlineData("with space", "with_space")]
    public void SanitizeIdentifier_HandlesCases(string input, string expected)
    {
        Assert.Equal(expected, Invoke("SanitizeIdentifier", input));
    }

    [Theory]
    [InlineData("getUser", "GetUser")]
    [InlineData("user-permissions-list", "UserPermissionsList")]
    [InlineData("", "Unknown")]
    [InlineData("API", "Api")]
    public void ToPascalCase_HandlesCases(string input, string expected)
    {
        Assert.Equal(expected, Invoke("ToPascalCase", input));
    }
}
