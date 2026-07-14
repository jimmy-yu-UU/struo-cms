using Struo.Domain.Auditing;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public sealed class SoftDeleteTests
{
    private sealed class Sample : ISoftDeletable
    {
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }
    }

    [Fact]
    public void ISoftDeletable_carries_deletion_marker()
    {
        var s = new Sample { DeletedAt = new DateTime(2026, 7, 14), DeletedBy = Guid.Empty };
        Assert.NotNull(s.DeletedAt);
        Assert.Equal(Guid.Empty, s.DeletedBy);
    }

    [Fact]
    public void DeletedFilter_has_three_modes()
    {
        Assert.Equal(0, (int)DeletedFilter.Exclude);
        Assert.Equal(3, System.Enum.GetValues<DeletedFilter>().Length);
    }
}
