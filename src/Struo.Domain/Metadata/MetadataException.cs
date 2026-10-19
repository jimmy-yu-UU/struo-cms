namespace Struo.Domain.Metadata;

public sealed class MetadataException : Exception
{
    public MetadataException(string message) : base(message) { }

    public MetadataException(string message, Exception innerException) : base(message, innerException) { }
}
