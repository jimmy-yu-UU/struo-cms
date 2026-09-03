using AwesomeAssertions;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Persistence;

public sealed class TranslationSidecarIndexPolicyTests
{
    [Fact]
    public void FromMetadata_derives_the_shipped_file_sidecar_key_and_group_name()
    {
        var collections = MetadataScanner.ScanTypes([typeof(File), typeof(MediaFolder)]);
        var policy = TranslationSidecarIndexPolicy.FromMetadata(collections);

        policy.UniqueGroupFor(typeof(FileTranslation), nameof(FileTranslation.FileId))
            .Should().Be("ux_file_translations_fk_locale");
        policy.UniqueGroupFor(typeof(FileTranslation), nameof(FileTranslation.Locale))
            .Should().Be("ux_file_translations_fk_locale");
    }

    [Fact]
    public void UniqueGroupFor_returns_null_for_non_key_properties_and_non_sidecar_types()
    {
        var collections = MetadataScanner.ScanTypes([typeof(File), typeof(MediaFolder)]);
        var policy = TranslationSidecarIndexPolicy.FromMetadata(collections);

        policy.UniqueGroupFor(typeof(FileTranslation), nameof(FileTranslation.Title)).Should().BeNull();
        policy.UniqueGroupFor(typeof(File), nameof(File.Id)).Should().BeNull();
    }

    [Fact]
    public void None_matches_nothing()
    {
        TranslationSidecarIndexPolicy.None
            .UniqueGroupFor(typeof(FileTranslation), nameof(FileTranslation.FileId))
            .Should().BeNull();
    }

    [Fact]
    public void Group_name_falls_back_to_the_lowercased_type_name_without_SugarTable()
    {
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(UnnamedSidecar)] = TranslationSidecarIndexPolicy.KeyFor(typeof(UnnamedSidecar), "ParentId", "Locale"),
        });

        policy.UniqueGroupFor(typeof(UnnamedSidecar), "ParentId").Should().Be("ux_unnamedsidecar_fk_locale");
    }

    private sealed class UnnamedSidecar
    {
        public long Id { get; set; }
        public Guid ParentId { get; set; }
        public string Locale { get; set; } = "";
    }
}
