using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Domain.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Metadata;
using Xunit;
using static Struo.Tests.Metadata.BrokenAssemblyFixture;

namespace Struo.Tests.Metadata;

// A content assembly with a type that cannot load stops startup with a MetadataException that names
// the assembly and what failed, on both paths that read an assembly's types.
public class AssemblyLoadFailureTests
{
    [Fact]
    public void The_fixture_raises_ReflectionTypeLoadException_from_GetTypes()
    {
        using var fixture = new BrokenAssemblyFixture(Defect.MissingBaseClass);

        var act = () => fixture.Assembly.GetTypes();

        act.Should().Throw<ReflectionTypeLoadException>()
            .Which.LoaderExceptions.Should().ContainSingle()
            .Which.Should().BeOfType<FileNotFoundException>().Which.Message.Should().Contain(fixture.DependencyName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadTypes_throws_MetadataException_naming_the_assembly_and_the_loader_error(bool publicBrokenType)
    {
        using var fixture = new BrokenAssemblyFixture(Defect.MissingBaseClass, publicBrokenType);

        var act = () => MetadataScanner.LoadTypes(fixture.Assembly);

        act.Should().Throw<MetadataException>()
            .WithMessage($"*'{fixture.AssemblyName}'*1 of 2 types*{fixture.DependencyName}*");
    }

    [Fact]
    public void Scan_throws_the_same_MetadataException()
    {
        using var fixture = new BrokenAssemblyFixture(Defect.MissingBaseClass);

        var act = () => MetadataScanner.Scan(fixture.Assembly);

        act.Should().Throw<MetadataException>().WithMessage($"*'{fixture.AssemblyName}'*{fixture.DependencyName}*");
    }

    [Fact]
    public void AddStruoMetadata_throws_the_same_MetadataException()
    {
        using var fixture = new BrokenAssemblyFixture(Defect.MissingBaseClass, publicBrokenType: false);

        var act = () => new ServiceCollection().AddStruoMetadata(fixture.Assembly);

        act.Should().Throw<MetadataException>().WithMessage($"*'{fixture.AssemblyName}'*{fixture.DependencyName}*");
    }

    [Fact]
    public void Several_failing_types_with_the_same_cause_list_the_cause_once_with_a_count()
    {
        using var fixture = new BrokenAssemblyFixture(Defect.MissingBaseClass, brokenTypeCount: 4);

        var act = () => MetadataScanner.LoadTypes(fixture.Assembly);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("4 of 5 types").And.Contain("(x4)");
        message.Split(fixture.DependencyName).Length.Should().Be(2, "the cause appears once");
    }

    [Fact]
    public void LoadTypes_returns_every_type_of_a_healthy_assembly()
    {
        var types = MetadataScanner.LoadTypes(typeof(Struo.Tests.Fixtures.FxArticle).Assembly).ToList();

        types.Should().Contain(typeof(Struo.Tests.Fixtures.FxArticle));
    }

    [Fact]
    public void A_type_that_loads_but_cannot_be_inspected_names_the_assembly_and_the_type()
    {
        using var fixture = new BrokenAssemblyFixture(Defect.MissingPropertyType);
        var types = MetadataScanner.LoadTypes(fixture.Assembly).ToList();
        var broken = types.Single(t => t.FullName == BrokenTypeName);

        var scan = () => MetadataScanner.Scan(fixture.Assembly);
        var scanTypes = () => MetadataScanner.ScanTypes(types);
        var descriptors = () => MetadataScanner.ScanDescriptors(types);

        foreach (var act in new Action[] { () => scan(), () => scanTypes(), () => descriptors() })
            act.Should().Throw<MetadataException>()
                .WithMessage($"*'{BrokenTypeName}'*'{fixture.AssemblyName}'*{fixture.DependencyName}*")
                .Which.InnerException.Should().BeAssignableTo<IOException>();
        broken.Assembly.Should().BeSameAs(fixture.Assembly);
    }

    [Fact]
    public void A_healthy_collection_type_is_unaffected_by_the_inspection_guard()
    {
        var collections = MetadataScanner.ScanTypes([typeof(Struo.Tests.Fixtures.FxCategory)]);

        collections.Should().ContainSingle(c => c.Name == "fxCategory");
    }

    [Fact]
    public void Summary_names_failed_types_the_loader_reports_and_counts_the_unnamed_ones()
    {
        var typeLoad = CaptureTypeLoadException("Some.Missing.Type");
        var failure = new ReflectionTypeLoadException(
            [typeof(int), null, null, null],
            [typeLoad, new FileNotFoundException("dependency one"), new FileNotFoundException("dependency one")]);

        var message = AssemblyLoadFailure.ForAssembly(typeof(int).Assembly, failure).Message;

        message.Should().Contain("3 of 4 types").And.Contain("Some.Missing.Type")
            .And.Contain("dependency one (x2)");
    }

    [Fact]
    public void Summary_lists_at_most_five_distinct_errors_and_counts_the_rest()
    {
        var errors = Enumerable.Range(1, 8).Select(i => (Exception)new FileNotFoundException("error " + i)).ToArray();
        var failure = new ReflectionTypeLoadException([null, null, null, null, null, null, null, null], errors);

        var message = AssemblyLoadFailure.ForAssembly(typeof(int).Assembly, failure).Message;

        message.Should().Contain("error 5").And.NotContain("error 6").And.Contain("and 3 more");
    }

    [Fact]
    public void Summary_survives_null_loader_exceptions()
    {
        var failure = new ReflectionTypeLoadException([null], [null]);

        var act = () => AssemblyLoadFailure.ForAssembly(typeof(int).Assembly, failure);

        act.Should().NotThrow().Which.Message.Should().Contain("1 of 1 types");
    }

    [Fact]
    public void A_type_with_an_attribute_that_cannot_be_resolved_names_the_assembly_and_the_type()
    {
        using var fixture = new BrokenAssemblyFixture(Defect.MissingAttributeType);
        var types = MetadataScanner.LoadTypes(fixture.Assembly).ToList();

        var scan = () => MetadataScanner.Scan(fixture.Assembly);
        var scanTypes = () => MetadataScanner.ScanTypes(types);
        var descriptors = () => MetadataScanner.ScanDescriptors(types);
        var typeMap = () => MetadataScanner.CollectionTypeMap(types);

        foreach (var act in new Action[] { () => scan(), () => scanTypes(), () => descriptors(), () => typeMap() })
            act.Should().Throw<MetadataException>()
                .WithMessage($"*'{BrokenTypeName}'*'{fixture.AssemblyName}'*{fixture.DependencyName}*");
    }

    [Fact]
    public void The_message_says_Assembly_for_every_assembly_it_scans()
    {
        using var fixture = new BrokenAssemblyFixture(Defect.MissingBaseClass);

        var act = () => MetadataScanner.LoadTypes(fixture.Assembly);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Assembly '{fixture.AssemblyName}'").And.NotContain("Content assembly");
    }

    // The inspection guard classifies by exception type; these three are raised by the runtime when a
    // dependency exists but a member or an attribute blob does not match, which no emitted fixture here
    // reproduces, so the exceptions are thrown from the inspected delegate.
    [Theory]
    [InlineData(typeof(MissingMethodException))]
    [InlineData(typeof(MissingFieldException))]
    [InlineData(typeof(MissingMemberException))]
    [InlineData(typeof(CustomAttributeFormatException))]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(FileLoadException))]
    [InlineData(typeof(BadImageFormatException))]
    [InlineData(typeof(TypeLoadException))]
    public void Inspect_turns_a_load_failure_into_a_MetadataException_naming_the_type(Type failure)
    {
        var thrown = (Exception)Activator.CreateInstance(failure, "the member is gone")!;

        var act = () => AssemblyLoadFailure.Inspect<int>(typeof(string), () => throw thrown);

        act.Should().Throw<MetadataException>()
            .WithMessage("*'System.String'*'System.Private.CoreLib'*the member is gone*")
            .Which.InnerException.Should().BeSameAs(thrown);
    }

    [Fact]
    public void Inspect_lets_any_other_exception_through()
    {
        var act = () => AssemblyLoadFailure.Inspect<int>(typeof(string), () => throw new InvalidOperationException("other"));

        act.Should().Throw<InvalidOperationException>().WithMessage("other");
    }

    [Fact]
    public void Summary_lists_at_most_five_failed_type_names_and_counts_the_rest()
    {
        var typeLoads = Enumerable.Range(1, 8).Select(i => (Exception)CaptureTypeLoadException("Some.Missing.Type" + i));
        var failure = new ReflectionTypeLoadException(Enumerable.Repeat<Type?>(null, 8).ToArray(), typeLoads.ToArray());

        var message = AssemblyLoadFailure.ForAssembly(typeof(int).Assembly, failure).Message;

        message.Should().MatchRegex(
            @"cannot be loaded \(Some\.Missing\.Type1, Some\.Missing\.Type2, Some\.Missing\.Type3, " +
            @"Some\.Missing\.Type4, Some\.Missing\.Type5, and 3 more\)");
    }

    [Fact]
    public void The_fixture_removes_its_directory_when_construction_fails()
    {
        var parent = Path.Combine(Path.GetTempPath(), "struo-fixture-parent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var act = () => new BrokenAssemblyFixture((Defect)99, baseDirectory: parent);

            act.Should().Throw<ArgumentOutOfRangeException>();
            Directory.GetDirectories(parent).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static TypeLoadException CaptureTypeLoadException(string typeName)
    {
        try
        {
            Type.GetType(typeName + ", " + typeof(int).Assembly.GetName().Name, throwOnError: true);
        }
        catch (TypeLoadException ex)
        {
            return ex;
        }
        throw new InvalidOperationException("the runtime resolved a type that does not exist");
    }
}
