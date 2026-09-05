// tests/Struo.Tests/Query/ReservedFieldNameTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Query;

public class ReservedFieldNameTests
{
    [SugarTable("rf_bad")]
    [CmsCollection("Rf bad")]
    private sealed class RfBad
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Some", Interface = FieldInterface.Text)] public string? Some { get; set; }
    }

    [Fact]
    public void A_field_named_like_a_reserved_token_fails_the_scan()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(RfBad)]);
        act.Should().Throw<MetadataException>().WithMessage("*'some'*reserved*");
    }
}
