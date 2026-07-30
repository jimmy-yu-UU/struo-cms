namespace Struo.Application.Files;

/// <summary>The framework's media-library collection. Its rows are owned by the file-storage
/// pipeline (blob + derived metadata), not by the generic item write path, so several layers need to
/// name it: RBAC decisions in the Api layer and the generic-create guard in ItemService.</summary>
public static class FileCollection
{
    public const string Name = "file";
}
