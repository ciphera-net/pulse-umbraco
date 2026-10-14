using Ciphera.Pulse.Umbraco;
using Xunit;

namespace Ciphera.Pulse.Umbraco.Tests;

/// <summary>
/// Pure-logic tests — no ASP.NET Core host, no Umbraco. These assert the
/// exact bytes <see cref="TagBuilder.BuildScriptsHtml"/> returns, the same
/// way the Drupal module's kernel test asserts the exact HTML its renderer
/// produces: the canonical tag is a published shape, and "the render array
/// looked right" is not the same claim as "the bytes on the wire are right".
/// </summary>
public class TagBuilderTests
{
    private const string Core = "https://js.ciphera.net/script.js";
    private const string Companion = "https://js.ciphera.net/script.interactions.js";

    [Fact]
    public void Domain_only_produces_the_canonical_tag()
    {
        var html = TagBuilder.BuildScriptsHtml("example.com", companion: false, apiOrigin: "");

        Assert.Equal(
            $"<script defer data-domain=\"example.com\" src=\"{Core}\"></script>",
            html);
    }

    [Fact]
    public void Empty_domain_omits_data_domain_rather_than_emitting_it_blank()
    {
        var html = TagBuilder.BuildScriptsHtml("", companion: false, apiOrigin: "");

        Assert.Equal($"<script defer src=\"{Core}\"></script>", html);
        Assert.DoesNotContain("data-domain", html);
    }

    [Fact]
    public void Null_domain_behaves_like_empty_domain()
    {
        var html = TagBuilder.BuildScriptsHtml(null, companion: false, apiOrigin: null);

        Assert.Equal($"<script defer src=\"{Core}\"></script>", html);
    }

    [Fact]
    public void Invalid_domain_is_dropped_never_injected()
    {
        // The exact shape of a saved-XSS attempt: if this ever leaked into
        // the attribute unescaped, the quote would close "data-domain" early
        // and "onload" would become a live attribute on the <script> tag.
        var html = TagBuilder.BuildScriptsHtml("bad\" onload=\"alert(1)", companion: false, apiOrigin: "");

        Assert.Equal($"<script defer src=\"{Core}\"></script>", html);
        Assert.DoesNotContain("onload", html);
        Assert.DoesNotContain("alert", html);
    }

    [Fact]
    public void Domain_with_scheme_path_and_query_is_normalised()
    {
        var html = TagBuilder.BuildScriptsHtml("HTTPS://Example.COM/path?query=1", companion: false, apiOrigin: "");

        Assert.Equal(
            $"<script defer data-domain=\"example.com\" src=\"{Core}\"></script>",
            html);
    }

    [Fact]
    public void Companion_adds_a_second_request_with_no_domain_of_its_own()
    {
        var html = TagBuilder.BuildScriptsHtml("example.com", companion: true, apiOrigin: "");

        Assert.Equal(
            $"<script defer data-domain=\"example.com\" src=\"{Core}\"></script>\n" +
            $"<script defer src=\"{Companion}\"></script>",
            html);
    }

    [Fact]
    public void Domain_api_and_companion_together()
    {
        var html = TagBuilder.BuildScriptsHtml("chosen.dev", companion: true, apiOrigin: "https://proxy.example.com/");

        Assert.Equal(
            "<script defer data-domain=\"chosen.dev\" data-api=\"https://proxy.example.com\" " +
            $"src=\"{Core}\"></script>\n" +
            $"<script defer src=\"{Companion}\"></script>",
            html);
    }

    [Fact]
    public void Api_origin_trailing_slash_is_stripped()
    {
        var html = TagBuilder.BuildScriptsHtml("", companion: false, apiOrigin: "https://proxy.example.com/");

        Assert.Contains("data-api=\"https://proxy.example.com\"", html);
        Assert.DoesNotContain("proxy.example.com/\"", html);
    }

    [Fact]
    public void Script_tags_are_never_self_closed()
    {
        // A self-closed <script> is invalid HTML and every browser treats it
        // as an opening tag that swallows the rest of <head> — the same
        // lesson the Drupal TagBuilder documents as '#value' => ''.
        var html = TagBuilder.BuildScriptsHtml("example.com", companion: true, apiOrigin: "");

        Assert.DoesNotContain("/>", html);
        Assert.Equal(2, CountOccurrences(html, "></script>"));
    }

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("sub.example.com", true)]
    [InlineData("example.co.uk", true)]
    [InlineData("xn--exmple-cua.com", true)] // punycode label shape
    [InlineData("", false)]
    [InlineData("localhost", false)] // no dot — not what the tracker expects
    [InlineData("-example.com", false)] // leading hyphen
    [InlineData("example..com", false)] // empty label
    [InlineData("example.com/", false)] // bare input, not run through NormalizeDomain first
    [InlineData("exa mple.com", false)]
    [InlineData("example.1com", false)] // the TLD label must be letters only
    public void IsValidDomain_matches_the_documented_grammar(string domain, bool expected)
    {
        Assert.Equal(expected, TagBuilder.IsValidDomain(domain));
    }

    [Fact]
    public void NormalizeDomain_keeps_a_leading_www()
    {
        Assert.Equal("www.example.com", TagBuilder.NormalizeDomain("WWW.Example.com"));
    }

    [Fact]
    public void NormalizeDomain_strips_scheme_path_query_fragment_port_and_trailing_dot()
    {
        Assert.Equal("example.com", TagBuilder.NormalizeDomain("https://example.com:8443/some/path?x=1#y"));
        Assert.Equal("example.com", TagBuilder.NormalizeDomain("example.com."));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
