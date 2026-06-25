// tests/Struo.Tests/Support/ApiIntegrationCollection.cs
using Xunit;

namespace Struo.Tests.Support;

[CollectionDefinition("ApiIntegration")]
public sealed class ApiIntegrationCollection : ICollectionFixture<ApiFactory>;
