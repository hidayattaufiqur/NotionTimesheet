namespace NotionTimesheet.Services;

internal sealed record TokenResolution(string? Token, string? Source, string? Problem)
{
    public bool HasToken => !string.IsNullOrWhiteSpace(Token);
}

/// <summary>
/// Token source order: environment variable NOTION_API_KEY first, then
/// %LOCALAPPDATA%\CmdPalNotion\token.txt. The token is never compiled in, logged or
/// written by this extension.
/// </summary>
internal static class TokenStore
{
    public const string EnvironmentVariableName = "NOTION_API_KEY";

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CmdPalNotion");

    public static string TokenFile => Path.Combine(Folder, "token.txt");

    public static TokenResolution Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            var candidate = Clean(fromEnvironment);
            return LooksLikeToken(candidate)
                ? new TokenResolution(candidate, $"environment variable {EnvironmentVariableName}", null)
                : new TokenResolution(null, $"environment variable {EnvironmentVariableName}",
                    $"The {EnvironmentVariableName} environment variable is set but does not look like a Notion integration token (expected it to start with \"ntn_\" or \"secret_\").");
        }

        try
        {
            if (File.Exists(TokenFile))
            {
                var candidate = Clean(File.ReadAllText(TokenFile));
                if (LooksLikeToken(candidate))
                {
                    return new TokenResolution(candidate, TokenFile, null);
                }

                return new TokenResolution(null, TokenFile,
                    $"{TokenFile} exists but does not contain a Notion integration token (expected a single line starting with \"ntn_\" or \"secret_\").");
            }
        }
        catch (Exception ex)
        {
            return new TokenResolution(null, TokenFile, $"Could not read {TokenFile}: {ex.Message}");
        }

        return new TokenResolution(null, null,
            $"No Notion token found. Set the {EnvironmentVariableName} environment variable, or paste the token into {TokenFile}.");
    }

    private static string Clean(string value)
    {
        var trimmed = value.Trim().Trim('"', '\'');
        return trimmed;
    }

    private static bool LooksLikeToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.StartsWith("ntn_", StringComparison.Ordinal) || value.StartsWith("secret_", StringComparison.Ordinal));
}

