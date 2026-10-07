namespace Struo.Application.Metadata;

/// <summary>
/// Returns every CLR entity type that needs a database table, composed from scanned
/// collection metadata (collections + translation sidecars + M2M junctions) unioned with the
/// framework's own built-in entities. Consumed by the startup gate's schema check (Development), <c>migrate:check</c>
/// and <c>make:migration</c>.
/// </summary>
public interface IEntityTypeCollector
{
    IReadOnlyList<Type> CollectForInitTables();
}
