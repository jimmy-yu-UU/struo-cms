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
