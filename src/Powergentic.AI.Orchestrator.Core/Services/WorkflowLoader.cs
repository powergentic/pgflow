using Powergentic.AI.Orchestrator.Core.Abstractions;
using Powergentic.AI.Orchestrator.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Powergentic.AI.Orchestrator.Core.Services;

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
        var workflow = _deserializer.Deserialize<WorkflowDefinition>(yaml) ?? new WorkflowDefinition();
        return workflow;
    }
}
