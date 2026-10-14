using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Ciphera.Pulse.Umbraco;

/// <summary>
/// Builds the Pulse Analytics tag markup.
/// </summary>
/// <remarks>
/// Pure logic: no ASP.NET Core types, no Umbraco services, no DI — a string
/// goes in, a string of HTML comes out. That is deliberate, for two reasons.
///
/// First, it is unit-testable without an ASP.NET Core host, which is more
/// than the same split buys the Drupal module: Drupal's equivalent class
/// still needed a full kernel test to find out what its renderer did with
/// <c>'defer' =&gt; TRUE</c> (a bare <c>defer</c> there; a Docusaurus
/// minifier serialises the same intent as <c>defer="defer"</c> — two
/// platforms, same input, different bytes). A TagHelper's output buffer has
/// no such rendering layer in between: whatever this class returns is what
/// ships, byte for byte, so the one test suite is the proof for both logic
/// and bytes.
///
/// Second, the domain grammar, the URLs and the rule that the companion
/// script is a second request are copied from the other Pulse integrations
/// (Drupal, Joomla, TYPO3, Astro) rather than re-derived — two Pulse install
/// surfaces disagreeing about what the tag looks like is a drift bug waiting
/// to happen.
/// </remarks>
public static class TagBuilder
{
    /// <summary>The Pulse tracker.</summary>
    public const string ScriptUrl = "https://js.ciphera.net/script.js";

    /// <summary>The companion script: clicks, copies and form submits.</summary>
    public const string CompanionUrl = "https://js.ciphera.net/script.interactions.js";

    /// <summary>
    /// A registrable hostname. Labels of letters, digits and hyphens, at
    /// least one dot, a letter-only TLD. The value lands inside an HTML
    /// attribute and this shape cannot carry a quote, a space or a bracket —
    /// <see cref="BuildScriptsHtml"/> HTML-encodes it anyway, so this is the
    /// first of two guards, not the only one, the same relationship Drupal's
    /// TagBuilder has with Drupal's own attribute escaping.
    /// </summary>
    private static readonly Regex DomainPattern = new(
        @"^(?=.{1,253}$)(?!-)[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*\.[a-z]{2,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SchemePrefix = new(@"^[a-z][a-z0-9+.\-]*://", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PathOrQueryOrFragment = new(@"[/?#].*$", RegexOptions.Compiled);
    private static readonly Regex PortSuffix = new(@":.*$", RegexOptions.Compiled);

    /// <summary>
    /// Lower-cases a domain and strips a scheme, path, port and trailing dot.
    /// Keeps "www." — the Pulse site may be registered either way.
    /// </summary>
    public static string NormalizeDomain(string? input)
    {
        var value = (input ?? string.Empty).Trim().ToLowerInvariant();
        value = SchemePrefix.Replace(value, string.Empty);
        value = PathOrQueryOrFragment.Replace(value, string.Empty);
        value = PortSuffix.Replace(value, string.Empty);
        return value.TrimEnd('.');
    }

    /// <summary>Whether a normalised domain is a registrable hostname.</summary>
    public static bool IsValidDomain(string domain) => DomainPattern.IsMatch(domain);

    /// <summary>
    /// Builds the <c>&lt;script&gt;</c> tag(s) for the given configuration.
    /// </summary>
    /// <param name="domain">
    /// The domain this site is registered under in Pulse, or empty to let
    /// the tracker auto-detect the browser's hostname. An invalid non-empty
    /// domain is dropped rather than injected, and the tag falls back to
    /// auto-detection — the same working install, one attribute short.
    /// </param>
    /// <param name="companion">Whether to also load the companion script.</param>
    /// <param name="apiOrigin">A custom API origin, or empty for the default.</param>
    /// <returns>
    /// One or two <c>&lt;script&gt;</c> elements, newline-separated, already
    /// HTML-escaped and ready to write to the response. Never a self-closing
    /// <c>&lt;script /&gt;</c> — a self-closed script tag is invalid HTML and
    /// browsers treat it as an opening tag that swallows the rest of the
    /// head, the same lesson Drupal's TagBuilder carries as
    /// <c>'#value' =&gt; ''</c>.
    /// </returns>
    public static string BuildScriptsHtml(string? domain, bool companion, string? apiOrigin)
    {
        var sb = new StringBuilder();

        sb.Append("<script defer");

        var normalizedDomain = NormalizeDomain(domain);
        if (normalizedDomain.Length > 0 && IsValidDomain(normalizedDomain))
        {
            sb.Append(" data-domain=\"").Append(WebUtility.HtmlEncode(normalizedDomain)).Append('"');
        }

        var api = (apiOrigin ?? string.Empty).Trim().TrimEnd('/');
        if (api.Length > 0)
        {
            sb.Append(" data-api=\"").Append(WebUtility.HtmlEncode(api)).Append('"');
        }

        sb.Append(" src=\"").Append(ScriptUrl).Append("\"></script>");

        if (companion)
        {
            // No data-domain on the companion: the tracker resolves the
            // domain once, from the core script, and a second copy is a
            // second thing to keep in step.
            sb.Append('\n').Append("<script defer src=\"").Append(CompanionUrl).Append("\"></script>");
        }

        return sb.ToString();
    }
}
