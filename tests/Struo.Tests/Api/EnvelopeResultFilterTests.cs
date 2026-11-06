using System.IO;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Http;
using Xunit;

namespace Struo.Tests.Api;

public class EnvelopeResultFilterTests
{
    [Fact]
    public void Ok_object_becomes_success_envelope()
    {
        var res = EnvelopeResultFilter.BuildEnvelope(new OkObjectResult("hi"));
        var obj = res.Should().BeOfType<ObjectResult>().Subject;
        var env = obj.Value.Should().BeOfType<SuccessEnvelope>().Subject;
        env.Success.Should().BeTrue();
        env.Data.Should().Be("hi");
        env.Meta.Should().BeNull();
    }

    private static readonly int[] SingleItem = [1];

    [Fact]
    public void Paged_result_becomes_success_with_meta()
    {
        var res = EnvelopeResultFilter.BuildEnvelope(new OkObjectResult(new PagedResult(SingleItem, 42, 25, 5)));
        var env = res.Should().BeOfType<ObjectResult>().Subject.Value.Should().BeOfType<SuccessEnvelope>().Subject;
        env.Meta.Should().Be(new MetaInfo(42, 25, 5));
        env.Data.Should().BeEquivalentTo(SingleItem);
    }

    [Fact]
    public void Existing_error_envelope_is_left_untouched()
    {
        var original = new ObjectResult(Envelope.Error("VALIDATION", "x")) { StatusCode = 400 };
        EnvelopeResultFilter.BuildEnvelope(original).Should().BeNull();
    }

    [Fact]
    public void Existing_success_envelope_is_left_untouched()
    {
        EnvelopeResultFilter.BuildEnvelope(new OkObjectResult(Envelope.Success("x"))).Should().BeNull();
    }

    [Fact]
    public void Bare_not_found_becomes_not_found_error_envelope()
    {
        var res = EnvelopeResultFilter.BuildEnvelope(new NotFoundResult());
        var obj = res.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(404);
        obj.Value.Should().BeOfType<ErrorEnvelope>().Which.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void No_content_is_left_untouched() =>
        EnvelopeResultFilter.BuildEnvelope(new NoContentResult()).Should().BeNull();

    [Fact]
    public void File_result_is_left_untouched() =>
        EnvelopeResultFilter.BuildEnvelope(new FileStreamResult(Stream.Null, "image/png")).Should().BeNull();

    [Fact]
    public void Redirect_result_is_left_untouched() =>
        EnvelopeResultFilter.BuildEnvelope(new RedirectResult("https://example/x")).Should().BeNull();

    [Fact]
    public void Non_2xx_object_becomes_error_envelope_by_status()
    {
        var res = EnvelopeResultFilter.BuildEnvelope(new ObjectResult("boom") { StatusCode = 409 });
        var obj = res.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(409);
        obj.Value.Should().BeOfType<ErrorEnvelope>().Which.Error.Code.Should().Be("CONFLICT");
    }

    [Fact]
    public void Error_envelope_without_trace_id_is_stamped_with_the_given_one()
    {
        var original = new ObjectResult(Envelope.Error("VALIDATION", "x")) { StatusCode = 400 };

        var res = EnvelopeResultFilter.BuildEnvelope(original, "trace-1");

        var obj = res.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(400);
        var error = obj.Value.Should().BeOfType<ErrorEnvelope>().Subject.Error;
        error.Code.Should().Be("VALIDATION");
        error.TraceId.Should().Be("trace-1");
    }

    [Fact]
    public void Error_envelope_that_already_has_a_trace_id_is_left_untouched()
    {
        var original = new ObjectResult(Envelope.Error("VALIDATION", "x", traceId: "kept")) { StatusCode = 400 };
        EnvelopeResultFilter.BuildEnvelope(original, "trace-1").Should().BeNull();
    }

    [Fact]
    public void Bare_status_errors_carry_the_given_trace_id()
    {
        var bare = EnvelopeResultFilter.BuildEnvelope(new NotFoundResult(), "trace-2");
        bare.Should().BeOfType<ObjectResult>().Which.Value.Should().BeOfType<ErrorEnvelope>()
            .Which.Error.TraceId.Should().Be("trace-2");

        var withBody = EnvelopeResultFilter.BuildEnvelope(new ObjectResult("boom") { StatusCode = 409 }, "trace-3");
        withBody.Should().BeOfType<ObjectResult>().Which.Value.Should().BeOfType<ErrorEnvelope>()
            .Which.Error.TraceId.Should().Be("trace-3");
    }

    [Fact]
    public void Success_results_are_unaffected_by_a_trace_id() =>
        EnvelopeResultFilter.BuildEnvelope(new OkObjectResult(Envelope.Success("x")), "trace-4").Should().BeNull();
}
