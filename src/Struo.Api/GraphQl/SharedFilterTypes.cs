// src/Struo.Api/GraphQl/SharedFilterTypes.cs
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace Struo.Api.GraphQl;

/// <summary>Builds the reusable per-scalar operator input types shared by all collection filters.</summary>
internal static class SharedFilterTypes
{
    private const string StringScalar = "String";
    private const string BooleanScalar = "Boolean";
    private const string IsNullOperator = "isNull";
    private const string FloatScalar = "Float";
    private const string DateTimeScalar = "DateTime";

    internal static IEnumerable<ITypeSystemMember> Build()
    {
        yield return Input("StringFilter", ("eq", StringScalar), ("neq", StringScalar), ("in", "[String!]"), ("nin", "[String!]"),
            ("contains", StringScalar), ("startsWith", StringScalar), ("endsWith", StringScalar), (IsNullOperator, BooleanScalar));
        yield return Input("IntFilter", ("eq", "Int"), ("neq", "Int"), ("in", "[Int!]"), ("nin", "[Int!]"),
            ("lt", "Int"), ("lte", "Int"), ("gt", "Int"), ("gte", "Int"), (IsNullOperator, BooleanScalar));
        yield return Input("FloatFilter", ("eq", FloatScalar), ("neq", FloatScalar), ("in", "[Float!]"), ("nin", "[Float!]"),
            ("lt", FloatScalar), ("lte", FloatScalar), ("gt", FloatScalar), ("gte", FloatScalar), (IsNullOperator, BooleanScalar));
        yield return Input("DateTimeFilter", ("eq", DateTimeScalar), ("neq", DateTimeScalar), ("in", "[DateTime!]"), ("nin", "[DateTime!]"),
            ("lt", DateTimeScalar), ("lte", DateTimeScalar), ("gt", DateTimeScalar), ("gte", DateTimeScalar), (IsNullOperator, BooleanScalar));
        yield return Input("BooleanFilter", ("eq", BooleanScalar), ("neq", BooleanScalar), (IsNullOperator, BooleanScalar));
        yield return Input("IdFilter", ("eq", "ID"), ("neq", "ID"), ("in", "[ID!]"), ("nin", "[ID!]"), (IsNullOperator, BooleanScalar));
    }

    private static InputObjectType Input(string name, params (string field, string sdl)[] fields)
    {
        var config = new InputObjectTypeConfiguration(name, null, typeof(IReadOnlyDictionary<string, object?>));
        foreach (var (field, sdl) in fields)
            config.Fields.Add(new InputFieldConfiguration(field, null, TypeReference.Parse(sdl)));
        return InputObjectType.CreateUnsafe(config);
    }
}
