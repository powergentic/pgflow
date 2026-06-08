using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Powergentic.Flow.Actions.GitHubCopilot;

public static class CopilotAgentDiscovery
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IList<CustomAgentConfig> LoadAgentsFromWorkspace(string workingDirectory, ILogger logger)
    {
        var agentsDirectory = Path.Combine(workingDirectory, ".github", "agents");
        if (!Directory.Exists(agentsDirectory))
        {
            logger.LogInformation("No custom agents directory found at {AgentsDirectory}", agentsDirectory);
            return [];
        }

        var customAgents = new List<CustomAgentConfig>();
        foreach (var agentPath in Directory.EnumerateFiles(agentsDirectory, "*.agent.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var customAgent = ParseAgentFile(agentPath, workingDirectory);
            customAgents.Add(customAgent);
            logger.LogInformation("Loaded custom agent '{AgentName}' from {AgentPath}", customAgent.Name, agentPath);
        }

        logger.LogInformation("Loaded {AgentCount} custom agent(s)", customAgents.Count);
        return customAgents;
    }

    private static CustomAgentConfig ParseAgentFile(string path, string workingDirectory)
    {
        var text = File.ReadAllText(path);
        var (frontmatter, body) = SplitFrontmatter(text);
        var metadata = string.IsNullOrWhiteSpace(frontmatter)
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : YamlDeserializer.Deserialize<Dictionary<string, object?>>(frontmatter) ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var name = GetString(metadata, "name")
            ?? Path.GetFileNameWithoutExtension(path).Replace(".agent", string.Empty, StringComparison.OrdinalIgnoreCase);

        return new CustomAgentConfig
        {
            Name = name,
            DisplayName = GetString(metadata, "displayName") ?? name,
            Description = GetString(metadata, "description")
                ?? $"Agent loaded from {Path.GetRelativePath(workingDirectory, path)}",
            Prompt = body.Trim(),
            Tools = GetStringList(metadata, "tools"),
            Skills = GetStringList(metadata, "skills"),
            Model = GetString(metadata, "model"),
            Infer = GetBool(metadata, "infer"),
        };
    }

    private static (string? Frontmatter, string Body) SplitFrontmatter(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return (null, text);
        }

        using var reader = new StringReader(text);
        if (reader.ReadLine() != "---")
        {
            return (null, text);
        }

        var yamlLines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line == "---")
            {
                return (string.Join(Environment.NewLine, yamlLines), reader.ReadToEnd());
            }

            yamlLines.Add(line);
        }

        return (null, text);
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool? GetBool(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
            _ => null,
        };
    }

    private static IList<string>? GetStringList(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is string text)
        {
            var values = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return values.Length == 0 ? null : values;
        }

        if (value is IEnumerable<object?> objects)
        {
            var values = objects
                .Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList();

            return values.Count == 0 ? null : values;
        }

        return null;
    }
}
