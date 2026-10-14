namespace Ciphera.Pulse.Umbraco;

/// <summary>
/// Configuration for the Pulse Analytics tag, bound from the
/// <c>"PulseAnalytics"</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "PulseAnalytics": {
///     "Domain": "example.com",
///     "Companion": false,
///     "TrackPreview": false,
///     "ApiOrigin": ""
///   }
/// }
/// </code>
/// There is no backoffice settings screen for this — unlike the Drupal,
/// Joomla and TYPO3 integrations, which each add one, a plain .NET options
/// class bound from configuration is how an Umbraco site developer already
/// expects a package's settings to arrive (see
/// <c>Our.Umbraco.TagHelpers</c>, <c>SeoToolkit.Umbraco</c>): no new UI
/// surface, and the value lives in source control with the rest of the
/// site's configuration rather than in the database. See
/// <see cref="PulseAnalyticsOptionsValidator"/> for what "a mistyped domain
/// is rejected, not silently stored" becomes when there is no save button —
/// it is rejected at startup instead.
/// </remarks>
public sealed class PulseAnalyticsOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "PulseAnalytics";

    /// <summary>
    /// The domain this site is registered under in Pulse, e.g.
    /// <c>example.com</c>. Empty (the default) lets the tracker use the
    /// browser's own hostname, which is correct for a site served on one
    /// domain — set it explicitly when the site answers on several.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Also record clicks, copies and form submits. Off by default: this
    /// loads a second, separate script, and the core script's size is a
    /// published claim — nothing is folded into it to make this feature
    /// look free.
    /// </summary>
    public bool Companion { get; set; }

    /// <summary>
    /// Also fire the tracker while a content editor is previewing unpublished
    /// content in the backoffice. Off by default: a preview session is not
    /// the site's audience, and counting it inflates every number on the
    /// dashboard the same way an administration page would — this is the
    /// Umbraco-specific shape of the "also track administration pages"
    /// toggle in the Drupal, Joomla and TYPO3 integrations, which do not
    /// have a preview mode of their own to worry about.
    /// </summary>
    public bool TrackPreview { get; set; }

    /// <summary>
    /// Route events through your own proxy origin, e.g.
    /// <c>https://example.com</c>. A bare origin, not a full URL — the
    /// tracker appends its own path. Empty (the default) unless a proxy is
    /// set up.
    /// </summary>
    public string ApiOrigin { get; set; } = string.Empty;
}
