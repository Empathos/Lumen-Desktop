using System.IO;

namespace Lumen.App;

/// <summary>
/// Finds the OpenAI API key wherever the user plausibly put it:
/// process env → user env (setx, no restart needed) → machine env →
/// an `openai.key` file next to the exe or in any parent directory
/// (covers `dotnet run` from the repo root).
/// </summary>
internal static class ApiKeyLocator
{
    public static string? Resolve() => Find("OPENAI_API_KEY", "openai.key");

    public static string? ResolveGemini() => Find("GEMINI_API_KEY", "gemini.key");

    private static string? Find(string envVar, string fileName)
    {
        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            var v = Environment.GetEnvironmentVariable(envVar, target);
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate))
                {
                    var v = File.ReadAllText(candidate).Trim();
                    if (v.Length > 0) return v;
                }
            }
        }

        return null;
    }
}
