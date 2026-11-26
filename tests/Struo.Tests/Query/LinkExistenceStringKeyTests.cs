using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

// The existence check compares the ids a database returns with the ids sent. A string key is matched by the
// database's collation, which can ignore case, so the comparison ignores case for string keys only.
public sealed class LinkExistenceStringKeyTests
{
    private sealed class Coded
    {
        public string Id { get; set; } = "";
    }

    private sealed class Numbered
    {
        public long Id { get; set; }
    }

    private class Repository : DispatchProxy
    {
        public object[] Rows { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Name is "QueryWhereInAsync" or "QueryWhereInWithDeletedAsync"
                ? Task.FromResult<IReadOnlyList<object>>(Rows)
                : throw new NotSupportedException(targetMethod.Name);
    }

    private sealed class Registry(Type type) : IEntityRegistry
    {
        public EntityDescriptor? Get(string collection) =>
            new(type, new Dictionary<string, string>(), "Id");
    }

    private sealed class NoJunctions : IM2MDescriptorSource
    {
        public IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection) => [];
    }

    private static CollectionMetadata Meta() => new()
    {
        Name = "host", Label = "Host", FieldGroups = [], Fields = [],
    };

    private static Task Ensure(Type target, object row, object sent)
    {
        var repository = DispatchProxy.Create<IItemRepository, Repository>();
        ((Repository)(object)repository).Rows = [row];
        var check = new LinkExistenceCheck(repository, new Registry(target), new NoJunctions());
        var link = new PendingLink("codeId", "target", [sent], IsList: false);
        return check.EnsureAsync(Meta(), JsonDocument.Parse("{}").RootElement, [link], null, false, default);
    }

    [Fact]
    public async Task A_string_id_the_database_matched_by_case_is_found()
    {
        await Ensure(typeof(Coded), new Coded { Id = "ABC" }, "abc");
    }

    [Fact]
    public async Task A_string_id_no_row_matched_is_missing()
    {
        var act = () => Ensure(typeof(Coded), new Coded { Id = "XYZ" }, "abc");

        await Assert.ThrowsAsync<QueryException>(act);
    }

    [Fact]
    public async Task A_numeric_id_is_compared_as_it_is()
    {
        await Ensure(typeof(Numbered), new Numbered { Id = 7 }, 7L);
        var act = () => Ensure(typeof(Numbered), new Numbered { Id = 7 }, 8L);

        await Assert.ThrowsAsync<QueryException>(act);
    }
}
