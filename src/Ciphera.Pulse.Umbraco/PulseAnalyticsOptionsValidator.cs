using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Ciphera.Pulse.Umbraco;

/// <summary>
/// Validates <see cref="PulseAnalyticsOptions"/> at startup.
/// </summary>
/// <remarks>
/// Every other Pulse integration with a settings form (Drupal, Joomla,
/// TYPO3) rejects a mistyped domain at the moment it is saved, rather than
/// storing it and quietly shipping a broken tag: "an analytics install that
/// reports nothing while looking fine is the worst outcome this module can
/// produce" (the Drupal README). An Umbraco site has no save button here —
/// configuration lives in <c>appsettings.json</c> — so the equivalent
/// moment is application startup, and <see cref="PulseAnalyticsComposer"/>
/// wires this validator in with <c>ValidateOnStart()</c>: a malformed domain
/// fails the site's startup with this message, loudly, in the deploy log,
/// rather than serving an untracked site that looks fine. That is a
/// deliberate, stricter choice than falling back to auto-detection the way
/// a per-request failure would, because a startup failure is caught in CI or
/// on first deploy — before it reaches a visitor — and because "no silent
/// failures" is the house rule this package is built under.
/// </remarks>
public sealed class PulseAnalyticsOptionsValidator : IValidateOptions<PulseAnalyticsOptions>
{
    private static readonly Regex HttpOrHttpsScheme = new(@"^https?://", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ValidateOptionsResult Validate(string? name, PulseAnalyticsOptions options)
    {
        var failures = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.Domain))
        {
            var normalized = TagBuilder.NormalizeDomain(options.Domain);
            if (!TagBuilder.IsValidDomain(normalized))
            {
                failures.Add(
                    $"PulseAnalytics:Domain '{options.Domain}' is not a valid domain. " +
                    "Use a bare hostname such as 'example.com', or leave it empty to let " +
                    "the tracker auto-detect the browser's hostname.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.ApiOrigin) && !HttpOrHttpsScheme.IsMatch(options.ApiOrigin.Trim()))
        {
            failures.Add("PulseAnalytics:ApiOrigin must start with http:// or https://.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
