using System.Linq;
using Ciphera.Pulse.Umbraco;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ciphera.Pulse.Umbraco.Tests;

/// <summary>
/// <see cref="PulseAnalyticsComposer"/> wires this validator in with
/// <c>ValidateOnStart()</c>, so a failure here is what stops the SITE from
/// starting with a broken domain — see that class's remarks for why startup
/// is the chosen moment. These tests are therefore effectively the mutation
/// target for "a mistyped domain is rejected, not silently stored": prove it
/// fails for the wrong input before trusting that it passes the right one.
/// </summary>
public class PulseAnalyticsOptionsValidatorTests
{
    private readonly PulseAnalyticsOptionsValidator _validator = new();

    [Fact]
    public void Empty_domain_is_valid_it_means_auto_detect()
    {
        var result = _validator.Validate(null, new PulseAnalyticsOptions { Domain = "" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void A_real_domain_is_valid()
    {
        var result = _validator.Validate(null, new PulseAnalyticsOptions { Domain = "example.com" });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("not a domain")] // a space is never stripped by NormalizeDomain
    [InlineData("-example.com")]
    [InlineData("example")] // no dot
    public void A_mistyped_domain_fails_with_an_actionable_message(string domain)
    {
        var result = _validator.Validate(null, new PulseAnalyticsOptions { Domain = domain });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("PulseAnalytics:Domain") && f.Contains(domain));
    }

    [Fact]
    public void Empty_api_origin_is_valid_it_means_no_proxy()
    {
        var result = _validator.Validate(null, new PulseAnalyticsOptions { ApiOrigin = "" });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("https://proxy.example.com")]
    [InlineData("http://proxy.example.com")]
    public void An_api_origin_with_a_scheme_is_valid(string api)
    {
        var result = _validator.Validate(null, new PulseAnalyticsOptions { ApiOrigin = api });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("proxy.example.com")]
    [InlineData("ftp://proxy.example.com")]
    [InlineData("//proxy.example.com")]
    public void An_api_origin_without_http_or_https_fails(string api)
    {
        var result = _validator.Validate(null, new PulseAnalyticsOptions { ApiOrigin = api });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("PulseAnalytics:ApiOrigin"));
    }

    [Fact]
    public void Both_a_bad_domain_and_a_bad_api_origin_report_both_failures()
    {
        var result = _validator.Validate(null, new PulseAnalyticsOptions { Domain = "bad domain", ApiOrigin = "ftp://x" });

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures.Count());
    }
}
