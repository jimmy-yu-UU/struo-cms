// tests/Struo.Tests/Files/FileEntityShapeTests.cs
using AwesomeAssertions;
using Struo.Domain.Auditing;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Files;

public sealed class FileEntityShapeTests
{
    [Fact]
    public void The_file_entity_is_soft_deletable()
    {
        typeof(File).Should().Implement<ISoftDeletable>();
    }
}
