namespace Struo.Application.Configuration;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// Largest JSON request body in bytes (REST JSON endpoints and <c>/graphql</c>); more is a 413
    /// <c>PAYLOAD_TOO_LARGE</c>. File uploads are governed by <c>Struo:Files:MaxUploadBytes</c> instead.
    /// </summary>
    public long MaxJsonBodyBytes { get; set; } = 4_194_304;
}
