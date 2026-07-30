using SceneBuilder.Domain;

namespace SceneBuilder.Pipeline;

public sealed class ScenePartitionDraftFactory
{
    public SceneDraft Create(SceneDraft draft, ScenePartition partition)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(partition);
        var identifiers = partition.SemanticObjectIds.ToHashSet(StringComparer.Ordinal);
        return new SceneDraft
        {
            Id = draft.Id + ":partition:" + partition.Id,
            SourceDocument = draft.SourceDocument,
            SemanticObjects = draft.SemanticObjects.Where(item => identifiers.Contains(item.Id)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Nodes = draft.Nodes.Where(item => identifiers.Contains(item.SemanticObjectId)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Diagnostics = draft.Diagnostics
        };
    }
}
