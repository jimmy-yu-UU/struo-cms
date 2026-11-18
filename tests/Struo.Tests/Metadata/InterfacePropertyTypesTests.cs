using AwesomeAssertions;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

// The table of accepted property types has an entry for every FieldInterface member, so a new member
// has to be given its types explicitly.
public sealed class InterfacePropertyTypesTests
{
    [Fact]
    public void Every_member_has_an_entry()
    {
        Enum.GetValues<FieldInterface>().Where(m => !InterfacePropertyTypes.Has(m))
            .Should().BeEmpty("a new FieldInterface member needs an entry in InterfacePropertyTypes");
    }

    [Fact]
    public void A_member_outside_the_enum_has_no_entry()
    {
        InterfacePropertyTypes.Has((FieldInterface)999).Should().BeFalse();
        var act = () => InterfacePropertyTypes.Accepts((FieldInterface)999, typeof(string));
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Matching_is_exact()
    {
        InterfacePropertyTypes.Accepts(FieldInterface.MultiSelect, typeof(List<string>)).Should().BeTrue();
        InterfacePropertyTypes.Accepts(FieldInterface.MultiSelect, typeof(string[])).Should().BeFalse();
        InterfacePropertyTypes.Accepts(FieldInterface.MultiSelect, typeof(IList<string>)).Should().BeFalse();
        InterfacePropertyTypes.Accepts(FieldInterface.Number, typeof(short)).Should().BeFalse();
    }

    [Fact]
    public void Describe_and_Accepting_cover_every_member()
    {
        foreach (var member in Enum.GetValues<FieldInterface>())
            InterfacePropertyTypes.Describe(member).Should().NotBeNullOrWhiteSpace(member.ToString());
        InterfacePropertyTypes.Accepting(typeof(DateOnly)).Should().BeEmpty();
        InterfacePropertyTypes.Accepting(typeof(bool)).Should()
            .Equal(FieldInterface.Boolean, FieldInterface.Checkbox, FieldInterface.Hidden, FieldInterface.Divider);
    }
}
