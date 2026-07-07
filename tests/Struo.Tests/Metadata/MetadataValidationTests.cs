using AwesomeAssertions;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata
{
    public class MetadataValidationTests
    {
        [CmsCollection("BadDisplay", DefaultDisplayField = "Missing")]
        private sealed class BadDisplay
        {
            [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
        }

        [CmsCollection("BadOptions")]
        private sealed class BadOptions
        {
            [CmsField(Interface = FieldInterface.Text)]
            [CmsOptions("a:A")]
            public string Name { get; set; } = "";
        }

        [CmsCollection("MalformedOptions")]
        private sealed class MalformedOptions
        {
            [CmsField(Interface = FieldInterface.Select)]
            [CmsOptions(":noValueHere")]   // leading colon => empty value => still invalid
            public string Status { get; set; } = "";
        }

        [CmsCollection("BareOptions")]
        private sealed class BareOptions
        {
            [CmsField(Interface = FieldInterface.Select)]
            [CmsOptions("draft", "published:Published")] // bare "draft" => label defaults to "draft"
            public string Status { get; set; } = "";
        }

        [Fact]
        public void Throws_on_duplicate_collection_name()
        {
            var act = () => MetadataScanner.ScanTypes(
                [typeof(DupNs1.Collision), typeof(DupNs2.Collision)]);
            act.Should().Throw<MetadataException>().WithMessage("*Duplicate collection name*");
        }

        [Fact]
        public void Throws_when_default_display_field_unknown()
        {
            var act = () => MetadataScanner.ScanTypes([typeof(BadDisplay)]);
            act.Should().Throw<MetadataException>().WithMessage("*DefaultDisplayField*");
        }

        [Fact]
        public void Throws_when_cms_options_on_non_option_interface()
        {
            var act = () => MetadataScanner.ScanTypes([typeof(BadOptions)]);
            act.Should().Throw<MetadataException>().WithMessage("*not an option type*");
        }

        [Fact]
        public void Throws_when_cms_options_entry_has_empty_value()
        {
            var act = () => MetadataScanner.ScanTypes([typeof(MalformedOptions)]);
            act.Should().Throw<MetadataException>().WithMessage("*value is required*");
        }

        [Fact]
        public void Bare_option_entry_defaults_label_to_value()
        {
            var collections = MetadataScanner.ScanTypes([typeof(BareOptions)]);
            var field = collections.Single().Fields.Single(f => f.Name == "status");
            field.Options.Should().NotBeNull();
            field.Options!.Should().ContainSingle(o => o.Value == "draft" && o.Label == "draft");
            field.Options!.Should().ContainSingle(o => o.Value == "published" && o.Label == "Published");
        }
    }
}

namespace Struo.Tests.Metadata.DupNs1
{
    using Struo.Domain.Metadata.Attributes;
    using Struo.Domain.Metadata.Enums;

    [CmsCollection("Duplicate One")]
    internal sealed class Collision
    {
        [CmsField(Interface = FieldInterface.Text)]
        public string Name { get; set; } = "";
    }
}

namespace Struo.Tests.Metadata.DupNs2
{
    using Struo.Domain.Metadata.Attributes;
    using Struo.Domain.Metadata.Enums;

    [CmsCollection("Duplicate Two")]
    internal sealed class Collision
    {
        [CmsField(Interface = FieldInterface.Text)]
        public string Name { get; set; } = "";
    }
}
