using Powergentic.Flow.Core.Abstractions;
using Powergentic.Flow.Core.Models;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Powergentic.Flow.Core.Services;

public sealed class WorkflowLoader : IWorkflowLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public async Task<WorkflowDefinition> LoadAsync(string workflowFilePath, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(workflowFilePath);
        using var reader = new StreamReader(stream);
        var yaml = await reader.ReadToEndAsync(cancellationToken);

        EnsureRemovedPropertiesAreNotUsed(yaml);

        var workflow = _deserializer.Deserialize<WorkflowDefinition>(yaml) ?? new WorkflowDefinition();
        return workflow;
    }

    private static void EnsureRemovedPropertiesAreNotUsed(string yaml)
    {
        using var yamlReader = new StringReader(yaml);
        var yamlStream = new YamlStream();
        yamlStream.Load(yamlReader);

        if (yamlStream.Documents.Count == 0 || yamlStream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return;
        }

        if (!TryGetChild(root, "execution", out var executionNode) || executionNode is not YamlMappingNode execution)
        {
            return;
        }

        if (TryGetChild(execution, "startAt", out _))
        {
            throw new InvalidOperationException("execution.startAt is no longer supported. Workflows now always start at the first action defined.");
        }
    }

    private static bool TryGetChild(YamlMappingNode mapping, string key, out YamlNode? value)
    {
        foreach (var child in mapping.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                value = child.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
