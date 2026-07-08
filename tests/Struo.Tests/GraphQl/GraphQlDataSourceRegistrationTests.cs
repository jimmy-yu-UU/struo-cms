using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlDataSourceRegistrationTests
{
    [Fact]
    public void Adapter_implements_the_port()
        => Assert.True(typeof(IGraphQlDataSource).IsAssignableFrom(typeof(ItemServiceGraphQlDataSource)));
}
