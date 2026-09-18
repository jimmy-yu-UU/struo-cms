// tests/Struo.Tests/Template/ControllerPersistenceBoundaryTests.cs
using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// Layering guard: no controller performs its own ORM access. Every data concern the HTTP layer has
/// must go through an <c>Struo.Application</c> seam whose implementation lives in
/// <c>Struo.Infrastructure</c> — the pattern items (<c>IItemUseCases</c>/<c>IItemRepository</c>),
/// revisions (<c>IRevisionStore</c>), settings, and authentication (<c>IUserCredentialStore</c>,
/// <c>IRolePermissionStore</c>, <c>IExternalUserStore</c>) already follow.
/// <para>
/// Bypassing this seam has two concrete costs, not just tidiness. First, <c>IItemRepository</c> is
/// documented as the seam a fork reimplements to point at a different storage engine; a controller
/// injecting <c>ISqlSugarClient</c> directly and writing
/// <c>Insertable</c>/<c>Updateable</c>/<c>Deleteable</c>/<c>Ado.BeginTranAsync</c> calls inline
/// would silently make that untrue. Second, being off the repository path also misses its
/// audit-stamping and version-bump behavior — the defect <c>UserCredentialWriteAuditTests</c> pins.
/// </para>
/// A repo-shape check (it reads the controller sources on disk), so it lives beside
/// <see cref="TemplateInvariantsTests"/> rather than with the endpoint tests.
/// </summary>
public sealed class ControllerPersistenceBoundaryTests
{
    /// <summary>
    /// Markers of hand-rolled ORM access. Deliberately does NOT ban <c>Struo.Infrastructure</c>
    /// wholesale: three controllers legitimately depend on <c>Struo.Infrastructure.Files.FileService</c>
    /// (aliased, so the namespace's <c>File</c> type doesn't collide with <c>System.IO.File</c>).
    /// That is a concrete Infrastructure service rather than raw ORM — whether it too deserves an
    /// Application-layer seam is a separate question this guard takes no position on.
    /// </summary>
    private static readonly string[] ForbiddenMarkers =
    [
        "ISqlSugarClient",
        "SqlSugar.ISqlSugarClient",
    ];

    [Fact]
    public void No_controller_injects_the_sqlsugar_client()
    {
        var controllerDir = Path.Combine(FindApiDir()!, "Controllers");
        Directory.Exists(controllerDir).Should().BeTrue($"expected to find {controllerDir}");

        var files = Directory.GetFiles(controllerDir, "*.cs", SearchOption.AllDirectories);
        files.Should().NotBeEmpty("the guard is worthless if it finds no controllers to check");

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var marker in ForbiddenMarkers)
            {
                if (source.Contains(marker, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)} -> {marker}");
            }
        }

        offenders.Should().BeEmpty(
            "controllers must reach persistence through an Struo.Application seam, not by injecting " +
            "the SqlSugar client directly. Add (or extend) an interface in Struo.Application and its " +
            "SqlSugar implementation in Struo.Infrastructure, the way IUserCredentialStore / " +
            "IRolePermissionStore / IItemRepository already do.");
    }

    // Same walk-up-from-BaseDirectory approach as TemplateInvariantsTests.FindApiDir.
    private static string? FindApiDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Struo.Api");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
