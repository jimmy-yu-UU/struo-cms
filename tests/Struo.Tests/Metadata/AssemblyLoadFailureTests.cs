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
