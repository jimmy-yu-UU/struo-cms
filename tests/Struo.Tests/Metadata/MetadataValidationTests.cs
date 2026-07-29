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
            [CmsOptions("draft", "published:Published", "archived:")] // bare "draft" => label defaults to "draft"; "archived:" => blank label after colon also defaults to value
            public string Status { get; set; } = "";
        }

        [CmsCollection("EmptyOptionValue")]
        private sealed class EmptyOptionValue
        {
            [CmsField(Interface = FieldInterface.Select)]
            [CmsOptions("")]   // no colon, but the value itself is empty => invalid
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
        public void Throws_when_cms_options_entry_is_blank_with_no_colon()
        {
            var act = () => MetadataScanner.ScanTypes([typeof(EmptyOptionValue)]);
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
            field.Options!.Should().ContainSingle(o => o.Value == "archived" && o.Label == "archived");
        }

        // 7g+ slice 4: Repeater scan-time fail-fast guards.

        [CmsCollection("RepeaterNotAList")]
        private sealed class RepeaterNotAList
        {
            [CmsField(Interface = FieldInterface.Repeater)] public string Faqs { get; set; } = "";
        }

        private sealed class RichChild
        {
            [CmsField(Interface = FieldInterface.RichText)] public string Body { get; set; } = "";
        }
        [CmsCollection("RepeaterDisallowedSub")]
        private sealed class RepeaterDisallowedSub
        {
            [CmsField(Interface = FieldInterface.Repeater)] public List<RichChild> Rows { get; set; } = new();
        }

        private sealed class NestedChild
        {
            [CmsField(Interface = FieldInterface.Repeater)] public List<RichChild> Inner { get; set; } = new();
        }
        [CmsCollection("RepeaterNested")]
        private sealed class RepeaterNested
        {
            [CmsField(Interface = FieldInterface.Repeater)] public List<NestedChild> Rows { get; set; } = new();
        }

        private sealed class TranslatableChild
        {
            [CmsField(Interface = FieldInterface.Text, Translatable = true)] public string T { get; set; } = "";
        }
        [CmsCollection("RepeaterTranslatableSub")]
        private sealed class RepeaterTranslatableSub
        {
            [CmsField(Interface = FieldInterface.Repeater)] public List<TranslatableChild> Rows { get; set; } = new();
        }

        private sealed class EmptyChild { public string Bare { get; set; } = ""; }
        [CmsCollection("RepeaterEmptyChild")]
        private sealed class RepeaterEmptyChild
        {
            [CmsField(Interface = FieldInterface.Repeater)] public List<EmptyChild> Rows { get; set; } = new();
        }

        private sealed class OkChild
        {
            [CmsField(Interface = FieldInterface.Text)] public string A { get; set; } = "";
        }
        [CmsCollection("RepeaterTranslatableParent")]
        private sealed class RepeaterTranslatableParent
        {
            [CmsField(Interface = FieldInterface.Repeater, Translatable = true)] public List<OkChild> Rows { get; set; } = new();
        }

        private sealed class IListChild
        {
            [CmsField(Interface = FieldInterface.Text)] public string A { get; set; } = "";
        }
        [CmsCollection("RepeaterInterfaceTypedList")]
        private sealed class RepeaterInterfaceTypedList
        {
            [CmsField(Interface = FieldInterface.Repeater)] public IList<IListChild> Rows { get; set; } = new List<IListChild>();
        }

        [Fact]
        public void Throws_when_repeater_is_not_a_list() =>
            ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterNotAList)])))
                .Should().Throw<MetadataException>().WithMessage("*must be a List<T>*");

        [Fact]
        public void Throws_when_repeater_sub_field_interface_not_allowed() =>
            ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterDisallowedSub)])))
                .Should().Throw<MetadataException>().WithMessage("*not allowed inside a Repeater*");

        [Fact]
        public void Throws_when_repeater_nested_in_repeater() =>
            ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterNested)])))
                .Should().Throw<MetadataException>().WithMessage("*not allowed inside a Repeater*");

        [Fact]
        public void Throws_when_repeater_sub_field_translatable() =>
            ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterTranslatableSub)])))
                .Should().Throw<MetadataException>().WithMessage("*cannot be translatable*");

        [Fact]
        public void Throws_when_repeater_child_has_no_cms_fields() =>
            ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterEmptyChild)])))
                .Should().Throw<MetadataException>().WithMessage("*must declare at least one [CmsField]*");

        [Fact]
        public void Throws_when_repeater_parent_translatable() =>
            ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterTranslatableParent)])))
                .Should().Throw<MetadataException>().WithMessage("*cannot be translatable*");

        [Fact]
        public void Throws_when_repeater_is_interface_typed_list() =>
            ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterInterfaceTypedList)])))
                .Should().Throw<MetadataException>().WithMessage("*must be a List<T>*");
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
