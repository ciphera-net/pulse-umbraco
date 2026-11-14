using System.Linq;
using Ciphera.Pulse.Umbraco;
using Microsoft.Extensions.Logging;
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
    public void An_obsolete_Companion_value_is_valid_and_warns_once()
    {
        var logger = new RecordingLogger();
        var validator = new PulseAnalyticsOptionsValidator(logger);
#pragma warning disable CS0618 // Setting the obsolete option is the point of this test.
        var options = new PulseAnalyticsOptions { Domain = "example.com", Companion = true };
#pragma warning restore CS0618

        Assert.True(validator.Validate(null, options).Succeeded);
        Assert.True(validator.Validate(null, options).Succeeded);

        var warning = Assert.Single(logger.Warnings);
        Assert.Equal(
            "The Companion option has been ignored since 1.1.0: switch it in your site's settings in Pulse.",
            warning);
    }

    [Fact]
    public void No_warning_when_Companion_is_not_set()
    {
        var logger = new RecordingLogger();
        var validator = new PulseAnalyticsOptionsValidator(logger);

        Assert.True(validator.Validate(null, new PulseAnalyticsOptions { Domain = "example.com" }).Succeeded);
        Assert.Empty(logger.Warnings);
    }

    private sealed class RecordingLogger : ILogger<PulseAnalyticsOptionsValidator>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
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
