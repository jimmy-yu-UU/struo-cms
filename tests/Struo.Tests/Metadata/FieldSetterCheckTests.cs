using AwesomeAssertions;
using System.Text.Json.Serialization;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A writable <c>[CmsField]</c> needs a public setter, or the value written to it is not stored: the scan
/// refuses a collection field or repeater sub-field the write cannot bind unless it is <c>ReadOnly</c>, and a translation
/// field without any setter (a non-public setter on a translation field stores the value).
/// </summary>
public sealed class FieldSetterCheckTests
{
    // ── collection classes: one writable field per setter shape ──

    [CmsCollection("Fsc protected", Name = "fscProtected")]
    private class ProtectedSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; protected set; } = "";
    }

    [CmsCollection("Fsc internal", Name = "fscInternal")]
    private sealed class InternalSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; internal set; } = "";
    }

    [CmsCollection("Fsc private", Name = "fscPrivate")]
    private sealed class PrivateSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; private set; } = "";
    }

    [CmsCollection("Fsc none", Name = "fscNone")]
    private sealed class NoSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; } = "";
    }

    [CmsCollection("Fsc expression", Name = "fscExpression")]
    private sealed class ExpressionBodied
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X => "x";
    }

    [CmsCollection("Fsc ignored", Name = "fscIgnored")]
    private sealed class IgnoredPrivateSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "X", Interface = FieldInterface.Text)]
        [SugarColumn(IsIgnore = true)] public string X { get; private set; } = "";
    }

    // ── collection controls ──

    [CmsCollection("Fsc accepted", Name = "fscAccepted")]
    private sealed class AcceptedShapes
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Public", Interface = FieldInterface.Text)] public string Public { get; set; } = "";
        [CmsField(Label = "Init", Interface = FieldInterface.Text)] public string Init { get; init; } = "";
        [CmsField(Label = "Private get", Interface = FieldInterface.Text)] public string PrivateGet { private get; set; } = "";
    }

    [CmsCollection("Fsc read only", Name = "fscReadOnly")]
    private class ReadOnlyShapes
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Protected", Interface = FieldInterface.Text, ReadOnly = true)] public string? Protected { get; protected set; }
        [CmsField(Label = "Internal", Interface = FieldInterface.Text, ReadOnly = true)] public string? Internal { get; internal set; }
        [CmsField(Label = "Private", Interface = FieldInterface.Text, ReadOnly = true)] public string? Private { get; private set; }
        [CmsField(Label = "Bare", Interface = FieldInterface.Text, ReadOnly = true)] public string? Bare { get; }
        [CmsField(Label = "Expression", Interface = FieldInterface.Text, ReadOnly = true)] public string Expression => "x";
    }

    // ── translation classes ──

    [CmsCollection("Fsc tr none", Name = "fscTrNone")]
    private sealed class TrNoSetterParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrNoSetter))]
        [SugarColumn(IsIgnore = true)] public List<TrNoSetter> Translations { get; set; } = [];
    }

    private sealed class TrNoSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrNoSetterParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; } = "";
    }

    [CmsCollection("Fsc tr read only", Name = "fscTrReadOnly")]
    private sealed class TrReadOnlyNoSetterParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrReadOnlyNoSetter))]
        [SugarColumn(IsIgnore = true)] public List<TrReadOnlyNoSetter> Translations { get; set; } = [];
    }

    private sealed class TrReadOnlyNoSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrReadOnlyNoSetterParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "X", Interface = FieldInterface.Text, ReadOnly = true)] public string X { get; } = "";
    }

    [CmsCollection("Fsc tr private", Name = "fscTrPrivate")]
    private sealed class TrPrivateSetterParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrPrivateSetter))]
        [SugarColumn(IsIgnore = true)] public List<TrPrivateSetter> Translations { get; set; } = [];
    }

    private class TrPrivateSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrPrivateSetterParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Private", Interface = FieldInterface.Text)] public string Private { get; private set; } = "";
        [CmsField(Label = "Protected", Interface = FieldInterface.Text)] public string Protected { get; protected set; } = "";
        [CmsField(Label = "Internal", Interface = FieldInterface.Text)] public string Internal { get; internal set; } = "";
    }

    // ── repeater child types ──

    private class ChildProtected { [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; protected set; } = ""; }
    private sealed class ChildInternal { [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; internal set; } = ""; }
    private sealed class ChildPrivate { [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; private set; } = ""; }
    private sealed class ChildNone { [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; } = ""; }

    private sealed class ChildReadOnly
    {
        [CmsField(Label = "Private", Interface = FieldInterface.Text, ReadOnly = true)] public string Private { get; private set; } = "";
        [CmsField(Label = "Bare", Interface = FieldInterface.Text, ReadOnly = true)] public string Bare { get; } = "";
        [CmsField(Label = "Init", Interface = FieldInterface.Text)] public string Init { get; init; } = "";
    }

    [CmsCollection("Fsc rep protected", Name = "fscRepProtected")]
    private sealed class RepeaterProtected
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildProtected> Rows { get; set; } = [];
    }

    [CmsCollection("Fsc rep internal", Name = "fscRepInternal")]
    private sealed class RepeaterInternal
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildInternal> Rows { get; set; } = [];
    }

    [CmsCollection("Fsc rep private", Name = "fscRepPrivate")]
    private sealed class RepeaterPrivate
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildPrivate> Rows { get; set; } = [];
    }

    [CmsCollection("Fsc rep none", Name = "fscRepNone")]
    private sealed class RepeaterNone
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildNone> Rows { get; set; } = [];
    }

    [CmsCollection("Fsc rep read only", Name = "fscRepReadOnly")]
    private sealed class RepeaterReadOnly
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildReadOnly> Rows { get; set; } = [];
    }

    // ── repeater child types bound through a constructor ──

    private sealed class ChildCtor
    {
        public ChildCtor(string label) => Label = label;
        [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; }
    }

    private sealed record ChildRecord([property: CmsField(Label = "Label", Interface = FieldInterface.Text)] string Label);

    private sealed class ChildTwoConstructors
    {
        public ChildTwoConstructors() { }
        public ChildTwoConstructors(string label) => Label = label;
        [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; } = "";
    }

    private sealed class ChildOtherParameterName
    {
        public ChildOtherParameterName(string text) => Label = text;
        [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; }
    }

    private sealed class ChildOtherParameterType
    {
        public ChildOtherParameterType(int label) => Label = label.ToString();
        [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; }
    }

    private sealed class ChildJsonInclude
    {
        [JsonInclude, CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; private set; } = "";
    }

    [CmsCollection("Fsc rep ctor", Name = "fscRepCtor")]
    private sealed class RepeaterCtor
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildCtor> Rows { get; set; } = [];
        [CmsField(Label = "Records", Interface = FieldInterface.Repeater)] public List<ChildRecord> Records { get; set; } = [];
    }

    [CmsCollection("Fsc rep two ctors", Name = "fscRepTwoCtors")]
    private sealed class RepeaterTwoConstructors
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildTwoConstructors> Rows { get; set; } = [];
    }

    [CmsCollection("Fsc rep other name", Name = "fscRepOtherName")]
    private sealed class RepeaterOtherParameterName
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildOtherParameterName> Rows { get; set; } = [];
    }

    [CmsCollection("Fsc rep other type", Name = "fscRepOtherType")]
    private sealed class RepeaterOtherParameterType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildOtherParameterType> Rows { get; set; } = [];
    }

    [CmsCollection("Fsc rep json include", Name = "fscRepJsonInclude")]
    private sealed class RepeaterJsonInclude
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildJsonInclude> Rows { get; set; } = [];
    }

    // ── collection fields bound through [JsonInclude] ──

    [CmsCollection("Fsc json include", Name = "fscJsonInclude")]
    private class JsonIncludeShapes
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [JsonInclude, CmsField(Label = "Private", Interface = FieldInterface.Text)] public string Private { get; private set; } = "";
        [JsonInclude, CmsField(Label = "Protected", Interface = FieldInterface.Text)] public string Protected { get; protected set; } = "";
        [JsonInclude, CmsField(Label = "Internal", Interface = FieldInterface.Text)] public string Internal { get; internal set; } = "";
    }

    [CmsCollection("Fsc json include get", Name = "fscJsonIncludeGet")]
    private sealed class JsonIncludeGetOnly
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [JsonInclude, CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; } = "";
    }

    // ── translation class whose base class hides a setter ──

    private class TrBase
    {
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; private set; } = "";
    }

    private sealed class TrFromBase : TrBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrFromBaseParentId { get; set; }
        public string Locale { get; set; } = "";
    }

    [CmsCollection("Fsc tr base", Name = "fscTrBase")]
    private sealed class TrFromBaseParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrFromBase))]
        [SugarColumn(IsIgnore = true)] public List<TrFromBase> Translations { get; set; } = [];
    }

    [CmsCollection("Fsc derived", Name = "fscDerived")]
    private sealed class DerivedFromProtectedSetter : ProtectedSetter
    {
        public string Extra { get; set; } = "";
    }

    // ── tests ──

    private const string NoSetterTail =
        "has no setter, so a value written to it is not stored. If the server computes the value, " +
        "declare [CmsField(ReadOnly = true)]; otherwise add a public setter.";

    public static TheoryData<Type, string> RefusedWithoutSetter() => new()
    {
        { typeof(NoSetter), "NoSetter.X" },
        { typeof(ExpressionBodied), "ExpressionBodied.X" },
        { typeof(JsonIncludeGetOnly), "JsonIncludeGetOnly.X" },
        { typeof(RepeaterNone), "ChildNone.X" },
        { typeof(RepeaterTwoConstructors), "ChildTwoConstructors.Label" },
        { typeof(RepeaterOtherParameterName), "ChildOtherParameterName.Label" },
        { typeof(RepeaterOtherParameterType), "ChildOtherParameterType.Label" },
    };

    [Theory]
    [MemberData(nameof(RefusedWithoutSetter))]
    public void A_writable_field_without_a_setter_is_refused(Type owner, string field)
    {
        var scan = () => MetadataScanner.ScanTypes([owner]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Be($"Field '{field}' {NoSetterTail}");
    }

    public static TheoryData<Type, string, string> RefusedWithNonPublicSetter() => new()
    {
        { typeof(ProtectedSetter), "ProtectedSetter.X", "protected" },
        { typeof(InternalSetter), "InternalSetter.X", "internal" },
        { typeof(PrivateSetter), "PrivateSetter.X", "private" },
        { typeof(IgnoredPrivateSetter), "IgnoredPrivateSetter.X", "private" },
        { typeof(RepeaterProtected), "ChildProtected.X", "protected" },
        { typeof(RepeaterInternal), "ChildInternal.X", "internal" },
        { typeof(RepeaterPrivate), "ChildPrivate.X", "private" },
        { typeof(RepeaterJsonInclude), "ChildJsonInclude.Label", "private" },
    };

    [Theory]
    [MemberData(nameof(RefusedWithNonPublicSetter))]
    public void A_writable_field_with_a_non_public_setter_is_refused_and_names_it(Type owner, string field, string access)
    {
        var scan = () => MetadataScanner.ScanTypes([owner]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Be(
            $"Field '{field}' has a {access} setter, which a write does not call, so a value written to it is " +
            "not stored. Make the setter public; or, when server code sets the value, declare [CmsField(ReadOnly = true)].");
    }

    public static TheoryData<Type, string> RefusedTranslationFields() => new()
    {
        { typeof(TrNoSetterParent), "TrNoSetter.X" },
        { typeof(TrReadOnlyNoSetterParent), "TrReadOnlyNoSetter.X" },
    };

    [Theory]
    [MemberData(nameof(RefusedTranslationFields))]
    public void A_translation_field_without_a_setter_is_refused(Type owner, string field)
    {
        var scan = () => MetadataScanner.ScanTypes([owner]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Be(
            $"Field '{field}' has no setter, so writing a translation value to it fails. Add a setter.");
    }

    [Fact]
    public void A_translation_field_whose_setter_is_private_on_a_base_class_is_refused_and_says_so()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(TrFromBaseParent)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'TrBase.Title' has a private setter on the base class TrBase, which TrFromBase cannot reach. " +
            "Make it protected, init or public, or declare the field on TrFromBase.");
    }

    public static TheoryData<Type> Accepted() => new()
    {
        { typeof(AcceptedShapes) },
        { typeof(ReadOnlyShapes) },
        { typeof(TrPrivateSetterParent) },
        { typeof(RepeaterReadOnly) },
        { typeof(RepeaterCtor) },
        { typeof(JsonIncludeShapes) },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void A_public_setter_a_ReadOnly_field_or_a_binding_that_stores_is_accepted(Type owner)
    {
        var scan = () => MetadataScanner.ScanTypes([owner]);

        scan.Should().NotThrow();
    }

    [Fact]
    public void The_refusal_names_a_field_of_a_base_class_by_the_class_that_declares_it()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(DerivedFromProtectedSetter)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().StartWith("Field 'ProtectedSetter.X' has a protected setter");
    }
}
