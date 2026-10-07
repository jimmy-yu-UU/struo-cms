using FluentMigrator.Infrastructure;
using FluentMigrator.Runner;
using FluentMigrator.Runner.VersionTableInfo;
using FluentMigrator.Runner.Versioning;

namespace Struo.Tests.Migrations;

/// <summary>Delegates to a real loader and throws on the Nth call that records a version.</summary>
internal sealed class FailingVersionLoader(IVersionLoader inner, int failAtCall) : IVersionLoader
{
    private int _calls;

    public bool AlreadyCreatedVersionSchema => inner.AlreadyCreatedVersionSchema;
    public bool AlreadyCreatedVersionTable => inner.AlreadyCreatedVersionTable;
    public IMigrationRunner Runner { get => inner.Runner; set => inner.Runner = value; }
    public IVersionInfo VersionInfo { get => inner.VersionInfo; set => inner.VersionInfo = value; }
    public IVersionTableMetaData VersionTableMetaData => inner.VersionTableMetaData;

    public void DeleteVersion(long version) => inner.DeleteVersion(version);
    public IVersionTableMetaData GetVersionTableMetaData() => inner.GetVersionTableMetaData();
    public void LoadVersionInfo() => inner.LoadVersionInfo();
    public void RemoveVersionTable() => inner.RemoveVersionTable();
    public void UpdateVersionInfo(long version) => inner.UpdateVersionInfo(version);

    public void UpdateVersionInfo(long version, string description)
    {
        if (++_calls == failAtCall) throw new InvalidOperationException("injected failure");
        inner.UpdateVersionInfo(version, description);
    }
}
