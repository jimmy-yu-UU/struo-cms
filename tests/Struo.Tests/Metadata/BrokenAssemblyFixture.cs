using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Tests.Metadata;

/// <summary>
/// An emitted content assembly with a defective dependency, so some of its types cannot load. It is
/// built with <see cref="PersistedAssemblyBuilder"/> and loaded into its own collectible context; that
/// context resolves the dependency from the default one, which never finds the emitted file.
/// </summary>
internal sealed class BrokenAssemblyFixture : IDisposable
{
    internal enum Defect
    {
        /// <summary>
        /// A [CmsCollection] type derives from a class of an assembly that is not there: the type cannot load.
        /// </summary>
        MissingBaseClass,

        /// <summary>
        /// A [CmsCollection] type loads, but one of its properties has a type of the missing assembly.
        /// </summary>
        MissingPropertyType,

        /// <summary>A [CmsCollection] type loads, but it carries an attribute of the missing assembly.</summary>
        MissingAttributeType,
    }

    public const string GoodTypeName = "BrokenFixture.GoodThing";

    public const string BrokenTypeName = "BrokenFixture.BrokenThing";

    private readonly string _directory;
    private readonly AssemblyLoadContext _context = new("struo-broken-" + Guid.NewGuid().ToString("N"), isCollectible: true);

    public Assembly Assembly { get; }

    public string AssemblyName { get; }

    public string DependencyName { get; }

    public BrokenAssemblyFixture(
        Defect defect, bool publicBrokenType = true, int brokenTypeCount = 1, string? baseDirectory = null)
    {
        _directory = Path.Combine(baseDirectory ?? Path.GetTempPath(), "struo-broken-" + Guid.NewGuid().ToString("N"));
        var suffix = Guid.NewGuid().ToString("N")[..12];
        AssemblyName = "StruoBrokenFixture" + suffix;
        DependencyName = "StruoMissingDependency" + suffix;
        Directory.CreateDirectory(_directory);
        try
        {
            using var image = new MemoryStream(File.ReadAllBytes(Emit(defect, publicBrokenType, brokenTypeCount)));
            Assembly = _context.LoadFromStream(image);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _context.Unload();
        Directory.Delete(_directory, recursive: true);
    }

    private string Emit(Defect defect, bool publicBrokenType, int brokenTypeCount)
    {
        if (!Enum.IsDefined(defect)) throw new ArgumentOutOfRangeException(nameof(defect), defect, "unknown defect");

        var core = typeof(object).Assembly;
        var (missing, marker) = EmitDependency(core);

        var builder = new PersistedAssemblyBuilder(new AssemblyName(AssemblyName), core);
        var module = builder.DefineDynamicModule(AssemblyName);
        var collection = typeof(CmsCollectionAttribute).GetConstructor([typeof(string)])!;
        var name = typeof(CmsCollectionAttribute).GetProperty(nameof(CmsCollectionAttribute.Name))!;
        CustomAttributeBuilder Collection(string label, string collectionName) =>
            new(collection, [label], [name], [collectionName]);

        var good = DefineClass(module, GoodTypeName, TypeAttributes.Public, null);
        good.SetCustomAttribute(Collection("Good", "brokenGood"));
        good.CreateType();

        for (var i = 0; i < brokenTypeCount; i++)
        {
            var typeName = brokenTypeCount == 1 ? BrokenTypeName : BrokenTypeName + i;
            var visibility = publicBrokenType ? TypeAttributes.Public : TypeAttributes.NotPublic;
            var broken = defect == Defect.MissingBaseClass
                ? DefineClass(module, typeName, visibility, missing)
                : DefineClass(module, typeName, TypeAttributes.Public, null);
            if (defect == Defect.MissingPropertyType) AddProperty(broken, missing);
            if (defect == Defect.MissingAttributeType) broken.SetCustomAttribute(new CustomAttributeBuilder(marker, []));
            broken.SetCustomAttribute(Collection("Broken", "brokenThing" + i));
            broken.CreateType();
        }

        var assemblyPath = Path.Combine(_directory, AssemblyName + ".dll");
        builder.Save(assemblyPath);
        File.Delete(Path.Combine(_directory, DependencyName + ".dll"));
        return assemblyPath;
    }

    private (Type Type, ConstructorInfo MarkerConstructor) EmitDependency(Assembly core)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(DependencyName), core);
        var module = builder.DefineDynamicModule(DependencyName);
        var type = DefineClass(module, "Missing.Dependency", TypeAttributes.Public, null).CreateType();
        var marker = module.DefineType("Missing.MarkerAttribute", TypeAttributes.Public | TypeAttributes.Class, typeof(Attribute));
        var markerConstructor = marker.DefineDefaultConstructor(MethodAttributes.Public);
        marker.CreateType();
        builder.Save(Path.Combine(_directory, DependencyName + ".dll"));
        return (type, markerConstructor);
    }

    private static TypeBuilder DefineClass(ModuleBuilder module, string name, TypeAttributes visibility, Type? parent)
    {
        var type = module.DefineType(name, visibility | TypeAttributes.Class, parent);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type;
    }

    private static void AddProperty(TypeBuilder type, Type propertyType)
    {
        var field = type.DefineField("_other", propertyType, FieldAttributes.Private);
        var property = type.DefineProperty("Other", PropertyAttributes.None, propertyType, null);
        var getter = type.DefineMethod(
            "get_Other", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            propertyType, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);
    }
}
