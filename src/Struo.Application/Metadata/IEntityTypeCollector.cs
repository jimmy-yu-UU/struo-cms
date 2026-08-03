namespace Struo.Application.Metadata;

/// <summary>
/// Returns every CLR entity type that needs a database table, composed from scanned
/// collection metadata (collections + translation sidecars + M2M junctions) unioned with the
/// framework's own built-in entities. Consumed by <c>DatabaseInitializer.CreateMissingTables</c>,
/// which runs in every environment, on every backend.
/// </summary>
public interface IEntityTypeCollector
{
    IReadOnlyList<Type> CollectForInitTables();
}
