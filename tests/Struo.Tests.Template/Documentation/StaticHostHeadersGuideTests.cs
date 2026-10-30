using System.Text.RegularExpressions;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template.Documentation;

/// <summary>
/// Keeps the static-host <c>_headers</c> example in chapter 20 equal to the page headers the admin image's
/// nginx template sends. The example is the fenced <c>text</c> block whose first line is the comment
/// <c># _headers</c>; each of the zh-TW and en chapters carries one. Its <c>/*</c> rule must have the
/// same header names and values as the template's server-level <c>add_header</c> lines, except
/// Strict-Transport-Security (the template's value is a placeholder) and Content-Security-Policy, which
/// is compared directive by directive: the same directives, the same sources, the template's
/// <c>${...}</c> placeholders ignored, plus the documented API origin in <c>connect-src</c> and
/// <c>img-src</c>. The <c>/assets/*</c> rule carries the template's <c>Cache-Control</c> value for that
/// path. The example holds exactly the <c>/*</c> and <c>/assets/*</c> rules.
/// </summary>
public sealed partial class StaticHostHeadersGuideTests
{
    private const string ApiOrigin = "https://api.example.com";
    private const string Csp = "Content-Security-Policy";
    private const string Hsts = "Strict-Transport-Security";
    private const string CacheControl = "Cache-Control";
    private static readonly string[] ApiOriginDirectives = ["connect-src", "img-src"];
    private static readonly string[] GuideChapters =
        ["docs/guide/zh-TW/20-deployment.md", "docs/guide/en/20-deployment.md"];

    private const string NginxTemplate = "frontend/nginx/default.conf.template";

    [GeneratedRegex("""add_header\s+(?<name>[\w-]+)\s+"(?<value>[^"]*)"(?:\s+always)?\s*;""")]
    private static partial Regex AddHeader();

    [GeneratedRegex(@"```text\r?\n# _headers\r?\n(?<body>.*?)```", RegexOptions.Singleline)]
    private static partial Regex HeadersExample();

    [Theory]
    [MemberData(nameof(Chapters))]
    public void Example_security_headers_match_the_nginx_page_headers(string chapter)
    {
        var template = NginxPageHeaders();
        var rules = ReadExample(chapter);

        var star = rules.Should().ContainKey("/*").WhoseValue;
        star.Keys.Should().BeEquivalentTo(template.Keys, "the /* rule must send the template's page headers");

        foreach (var name in template.Keys.Where(n => n is not Csp and not Hsts))
            star[name].Should().Be(template[name], $"{name} is copied from the template");
        star[Hsts].Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Chapters))]
    public void Example_csp_has_the_template_directives_plus_the_api_origin(string chapter)
    {
        var expected = ParseCsp(NginxPageHeaders()[Csp]);
        var actual = ParseCsp(ReadExample(chapter)["/*"][Csp]);

        actual.Keys.Should().BeEquivalentTo(expected.Keys);
        foreach (var (directive, sources) in expected)
        {
            var allowed = ApiOriginDirectives.Contains(directive)
                ? sources.Append(ApiOrigin).ToArray()
                : sources;
            actual[directive].Should().BeEquivalentTo(allowed, $"CSP directive {directive}");
        }
    }

    [Theory]
    [MemberData(nameof(Chapters))]
    public void Example_cache_rules_match_the_nginx_cache_rules(string chapter)
    {
        var rules = ReadExample(chapter);

        rules.Keys.Should().BeEquivalentTo(["/*", "/assets/*"], "a rule the host never applies must not be documented");
        rules["/assets/*"][CacheControl]
            .Should().Be(NginxCacheValue(NginxText(), "location /assets/ {"));
    }

    [Fact]
    public void Every_nginx_page_location_sends_the_same_headers_as_the_server_level()
    {
        var nginx = NginxText();
        var server = NginxPageHeaders();

        foreach (var location in new[] { "location = /index.html {", "location /assets/ {", "location / {" })
        {
            var block = LocationBlock(nginx, location);
            var headers = AddHeader().Matches(block)
                .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);
            headers.Where(h => h.Key != CacheControl).Should().BeEquivalentTo(server, location);
        }
    }

    public static TheoryData<string> Chapters() => new(GuideChapters);

    private static string NginxText() => File.ReadAllText(Path.Combine(RepoRoot.Find(), NginxTemplate));

    // The add_header lines before the "location /api/" block are the server-level page headers.
    private static Dictionary<string, string> NginxPageHeaders()
    {
        var nginx = NginxText();
        var server = nginx[..nginx.IndexOf("location /api/ {", StringComparison.Ordinal)];
        var headers = AddHeader().Matches(server)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);
        headers.Should().NotBeEmpty();
        return headers;
    }

    private static string LocationBlock(string nginx, string opener)
    {
        var start = nginx.IndexOf(opener, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the template declares '{opener}'");
        var end = nginx.IndexOf("\n    }", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"'{opener}' closes with a 4-space-indented brace");
        return nginx[start..end];
    }

    private static string NginxCacheValue(string nginx, string opener) =>
        AddHeader().Matches(LocationBlock(nginx, opener))
            .Single(m => m.Groups["name"].Value == CacheControl).Groups["value"].Value;

    private static Dictionary<string, string[]> ParseCsp(string policy) =>
        policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(
                parts => parts[0],
                parts => parts.Skip(1).Where(s => !s.StartsWith("${", StringComparison.Ordinal)).ToArray());

    // path -> (header name -> value)
    private static Dictionary<string, Dictionary<string, string>> ReadExample(string chapter)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot.Find(), chapter));
        var match = HeadersExample().Match(text);
        match.Success.Should().BeTrue($"{chapter} carries a ```text block whose first line is '# _headers'");

        var rules = new Dictionary<string, Dictionary<string, string>>();
        Dictionary<string, string>? current = null;
        foreach (var raw in match.Groups["body"].Value.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
                continue;
            if (!char.IsWhiteSpace(line[0]))
            {
                current = new Dictionary<string, string>();
                rules[line.Trim()] = current;
                continue;
            }

            current.Should().NotBeNull("a header line follows a path line");
            var colon = line.IndexOf(':');
            colon.Should().BeGreaterThan(0, $"'{line}' is a 'Name: value' line");
            current![line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        return rules;
    }
}
