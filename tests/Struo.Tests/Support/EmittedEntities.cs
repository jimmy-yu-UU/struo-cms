using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Support;

/// <summary>
/// One <c>[CmsField]</c> property of an emitted type. <paramref name="Child"/> gives it the child type
/// instead of <paramref name="Type"/>: a <c>List</c> of it, or the child itself when
/// <paramref name="ChildIsList"/> is false. <paramref name="GetOnly"/> emits no setter.
/// <paramref name="Nullable"/> makes the column nullable.
/// </summary>
public sealed record EmittedProperty(
    string Name, Type? Type, FieldInterface Interface, bool WithOptions = false, EmittedChild? Child = null,
    bool ChildIsList = true, bool Hidden = false, bool ReadOnly = false, bool Translatable = false,
    bool GetOnly = false, bool Nullable = false);

/// <summary>
/// A many-to-one relation to the emitted entity <paramref name="Target"/> (declared earlier in the same
/// build), through the key property <paramref name="KeyProperty"/> of the owner.
/// </summary>
public sealed record EmittedRelation(string Name, string Target, string KeyProperty);

/// <summary>A class used as a Repeater child or as a structured value; a plain one declares no <c>[CmsField]</c>.</summary>
public sealed record EmittedChild(string Name, IReadOnlyList<EmittedProperty> Properties, bool Plain = false);

/// <summary>
/// A <c>[CmsCollection]</c> entity with a Guid <c>Id</c> and the given properties. <paramref name="Translations"/>
/// gives it a translation sidecar with those translatable properties, <paramref name="Relations"/> its many-to-one
/// relations and <paramref name="IdType"/> the type of its primary key (a Guid when null).
/// </summary>
public sealed record EmittedEntity(
    string Name, IReadOnlyList<EmittedProperty> Properties, IReadOnlyList<EmittedProperty>? Translations = null,
    IReadOnlyList<EmittedRelation>? Relations = null, Type? IdType = null);

/// <summary>
/// Builds collections from a description, so a test can declare one property per (interface, type) pair
/// without a class per pair. The assembly is saved and loaded like a compiled one, and a host can name it
/// in <c>Struo:ContentAssemblies</c>.
/// </summary>
public static class EmittedEntities
{
    private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.Ordinal);
    private static int _hooked;
    private static int _sequence;

    public static Assembly Build(string assemblyName, IReadOnlyList<EmittedEntity> entities)
    {
        var name = new AssemblyName(assemblyName);
        var assembly = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName);
        var children = new Dictionary<EmittedChild, TypeBuilder>();
        var entityTypes = new Dictionary<string, TypeBuilder>(StringComparer.Ordinal);

        foreach (var child in entities.SelectMany(e => e.Properties).Select(p => p.Child).OfType<EmittedChild>().Distinct())
        {
            var builder = DefineClass(module, child.Name, null);
            children[child] = builder;
            foreach (var property in child.Properties) AddProperty(builder, property, children, new PropertyShape(Plain: child.Plain));
            builder.CreateType();
        }

        foreach (var entity in entities)
        {
            var lower = char.ToLowerInvariant(entity.Name[0]) + entity.Name[1..];
            var builder = DefineClass(module, entity.Name, [
                Attribute(typeof(CmsCollectionAttribute), [entity.Name], Named(nameof(CmsCollectionAttribute.Name), lower)),
                Attribute(typeof(SugarTable), [lower]),
            ]);
            entityTypes[entity.Name] = builder;
            AddProperty(
                builder, new EmittedProperty("Id", entity.IdType ?? typeof(Guid), FieldInterface.Text), children, new PropertyShape(IsKey: true));
            foreach (var property in entity.Properties) AddProperty(builder, property, children);
            foreach (var relation in entity.Relations ?? []) AddRelation(builder, relation, entityTypes[relation.Target], children);
            if (entity.Translations is { } translated) AddSidecar(module, builder, entity.Name, translated, children);
            builder.CreateType();
        }

        using var stream = new MemoryStream();
        assembly.Save(stream);
        stream.Position = 0;
        EnsureHook();
        var loaded = AssemblyLoadContext.Default.LoadFromStream(stream);
        lock (Loaded) Loaded[assemblyName] = loaded;
        return loaded;
    }

    /// <summary>An assembly name no earlier call used.</summary>
    public static string UniqueName(string prefix) => $"{prefix}_{Guid.NewGuid().ToString("N")[..8]}_{Interlocked.Increment(ref _sequence)}";

    private static void AddSidecar(
        ModuleBuilder module, TypeBuilder owner, string ownerName, IReadOnlyList<EmittedProperty> fields,
        Dictionary<EmittedChild, TypeBuilder> children)
    {
        var sidecar = DefineClass(module, ownerName + "Translation", null);
        AddProperty(sidecar, new EmittedProperty("Id", typeof(Guid), FieldInterface.Text), children, new PropertyShape(IsKey: true));
        AddProperty(sidecar, new EmittedProperty(ownerName + "Id", typeof(Guid), FieldInterface.Text), children, new PropertyShape(Plain: true));
        AddProperty(sidecar, new EmittedProperty("Locale", typeof(string), FieldInterface.Text), children, new PropertyShape(Plain: true));
        foreach (var field in fields) AddProperty(sidecar, field with { Translatable = true }, children);
        sidecar.CreateType();

        var list = new EmittedProperty("Translations", typeof(object), FieldInterface.Text);
        AddProperty(
            owner, list, children,
            new PropertyShape(
                Plain: true, ListOf: sidecar,
                Extra: [Attribute(typeof(CmsTranslationsAttribute), [sidecar]), Attribute(typeof(SugarColumn), [], Named(nameof(SugarColumn.IsIgnore), true))]));
    }

    private static void AddRelation(
        TypeBuilder owner, EmittedRelation relation, TypeBuilder target, Dictionary<EmittedChild, TypeBuilder> children)
    {
        var navigate = new CustomAttributeBuilder(
            typeof(Navigate).GetConstructor([typeof(NavigateType), typeof(string), typeof(string[])])!,
            [NavigateType.ManyToOne, relation.KeyProperty, Array.Empty<string>()]);
        var cmsRelation = Attribute(
            typeof(CmsRelationAttribute), [], Named(nameof(CmsRelationAttribute.Interface), RelationInterface.Dropdown));
        var ignored = Attribute(typeof(SugarColumn), [], Named(nameof(SugarColumn.IsIgnore), true));
        AddProperty(
            owner, new EmittedProperty(relation.Name, typeof(object), FieldInterface.Text), children,
            new PropertyShape(Plain: true, AsType: target, Extra: [navigate, cmsRelation, ignored]));
    }

    private static TypeBuilder DefineClass(ModuleBuilder module, string name, CustomAttributeBuilder[]? attributes)
    {
        var builder = module.DefineType("Emitted." + name, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed, typeof(object));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        foreach (var attribute in attributes ?? []) builder.SetCustomAttribute(attribute);
        return builder;
    }

    // How a property differs from a plain field: the key, an undecorated property, a list of a type, extra
    // attributes, or a type given as a builder.
    private sealed record PropertyShape(
        bool IsKey = false, bool Plain = false, TypeBuilder? ListOf = null, CustomAttributeBuilder[]? Extra = null,
        TypeBuilder? AsType = null);

    private static void AddProperty(
        TypeBuilder owner, EmittedProperty spec, Dictionary<EmittedChild, TypeBuilder> children, PropertyShape? shape = null)
    {
        var (isKey, plain, listOf, extra, asType) = shape ?? new PropertyShape();
        var type = asType is not null ? asType
            : listOf is not null ? typeof(List<>).MakeGenericType(listOf)
            : spec.Child is not { } child ? spec.Type!
            : spec.ChildIsList ? typeof(List<>).MakeGenericType(children[child]) : children[child];
        var field = owner.DefineField("_" + spec.Name, type, FieldAttributes.Private);
        var property = owner.DefineProperty(spec.Name, PropertyAttributes.None, type, null);
        const MethodAttributes Accessor = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        var getter = owner.DefineMethod("get_" + spec.Name, Accessor, type, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
        if (!spec.GetOnly)
        {
            var setter = owner.DefineMethod("set_" + spec.Name, Accessor, null, [type]);
            il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            property.SetSetMethod(setter);
        }

        if (isKey)
        {
            property.SetCustomAttribute(Attribute(typeof(SugarColumn), [], Named(nameof(SugarColumn.IsPrimaryKey), true)));
            return;
        }

        foreach (var attribute in extra ?? []) property.SetCustomAttribute(attribute);
        if (spec.Nullable)
            property.SetCustomAttribute(Attribute(typeof(SugarColumn), [], Named(nameof(SugarColumn.IsNullable), true)));
        if (plain) return;
        property.SetCustomAttribute(Attribute(
            typeof(CmsFieldAttribute), [],
            Named(nameof(CmsFieldAttribute.Label), spec.Name), Named(nameof(CmsFieldAttribute.Interface), spec.Interface),
            Named(nameof(CmsFieldAttribute.Hidden), spec.Hidden), Named(nameof(CmsFieldAttribute.ReadOnly), spec.ReadOnly),
            Named(nameof(CmsFieldAttribute.Translatable), spec.Translatable)));
        if (spec.WithOptions)
            property.SetCustomAttribute(Attribute(typeof(CmsOptionsAttribute), [Options]));
    }

    private static readonly string[] Options = ["a:A", "b:B", "hello:Hello"];

    private static (string Name, object Value) Named(string name, object value) => (name, value);

    private static CustomAttributeBuilder Attribute(Type type, object[] constructorArguments, params (string Name, object Value)[] named)
    {
        var constructor = type.GetConstructors().Single(c => c.GetParameters().Length == constructorArguments.Length);
        var properties = named.Select(n => type.GetProperty(n.Name)!).ToArray();
        return new CustomAttributeBuilder(constructor, constructorArguments, properties, named.Select(n => n.Value).ToArray());
    }

    private static void EnsureHook()
    {
        if (Interlocked.Exchange(ref _hooked, 1) == 1) return;
        AssemblyLoadContext.Default.Resolving += (_, requested) =>
        {
            lock (Loaded) return requested.Name is null ? null : Loaded.GetValueOrDefault(requested.Name);
        };
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var requested = new AssemblyName(args.Name).Name!;
            lock (Loaded) return Loaded.GetValueOrDefault(requested);
        };
    }
}
