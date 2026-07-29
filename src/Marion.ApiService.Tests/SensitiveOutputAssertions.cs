using Xunit;

namespace Marion.ApiService.Tests;

internal static class SensitiveOutputAssertions
{
    private static readonly string[] SensitiveTerms =
    [
        "password",
        "connectionstring",
        "data source",
        "user id",
        "exception",
        "stack",
        "token"
    ];

    internal static void DoesNotContainSensitiveDetails(string content)
    {
        foreach (var term in SensitiveTerms)
        {
            Assert.DoesNotContain(term, content, StringComparison.OrdinalIgnoreCase);
        }
    }
}
