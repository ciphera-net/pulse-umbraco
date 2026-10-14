using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Cms.Core.Web;

namespace Ciphera.Pulse.Umbraco;

/// <summary>
/// Renders the Pulse Analytics tag. Add <c>&lt;pulse-analytics /&gt;</c>
/// inside <c>&lt;head&gt;</c> in your site's own layout.
/// </summary>
/// <remarks>
/// <code>
/// @addTagHelper *, Ciphera.Pulse.Umbraco
/// </code>
/// in <c>Views/_ViewImports.cshtml</c>, then
/// <code>
/// &lt;head&gt;
///   ...
///   &lt;pulse-analytics /&gt;
/// &lt;/head&gt;
/// </code>
/// in your layout. This is the same shape every real, accepted Umbraco
/// analytics/script package uses — a TagHelper the developer registers once
/// and places once — rather than middleware that rewrites a response it did
/// not render. The developer placing it is what makes the backoffice
/// exclusion structural: the backoffice is a separate single-page
/// application that never renders this, or any, front-end Razor layout, so
/// there is no code path through which this tag could appear there. The
/// explicit <see cref="IsBackofficeRequest"/> check below is defence in
/// depth on top of that, not instead of it.
/// </remarks>
[HtmlTargetElement("pulse-analytics")]
public sealed class PulseAnalyticsTagHelper : TagHelper
{
    private readonly IOptionsMonitor<PulseAnalyticsOptions> _options;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IHostingEnvironment _hostingEnvironment;

    public PulseAnalyticsTagHelper(
        IOptionsMonitor<PulseAnalyticsOptions> options,
        IUmbracoContextAccessor umbracoContextAccessor,
        IHostingEnvironment hostingEnvironment)
    {
        _options = options;
        _umbracoContextAccessor = umbracoContextAccessor;
        _hostingEnvironment = hostingEnvironment;
    }

    /// <summary>
    /// Bound by the framework on every TagHelper that needs the current
    /// request — the standard way to reach <see cref="HttpContext"/> from a
    /// TagHelper, since <see cref="TagHelperContext"/> itself carries none.
    /// </summary>
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        // <pulse-analytics> itself never reaches the response — only the
        // <script> tag(s) built below, or nothing.
        output.TagName = null;

        // MUTATE:BACKOFFICE_GUARD:BEGIN — scripts/verify.sh --mutate deletes
        // every line between these two markers (inclusive) to prove the
        // real-install harness actually fails when this guard is broken.
        // Keep both markers, each on its own line, if you touch this method.
        if (IsBackofficeRequest())
        {
            output.SuppressOutput();
            return;
        }
        // MUTATE:BACKOFFICE_GUARD:END

        var options = _options.CurrentValue;

        if (IsPreviewRequest() && !options.TrackPreview)
        {
            output.SuppressOutput();
            return;
        }

        var html = TagBuilder.BuildScriptsHtml(options.Domain, options.Companion, options.ApiOrigin);
        output.Content.SetHtmlContent(new HtmlString(html));
    }

    /// <summary>
    /// True for a request under the backoffice's own path — resolved through
    /// <see cref="IHostingEnvironment"/>'s own <c>GetBackOfficePath()</c>
    /// extension (<c>Umbraco.Cms.Core.Hosting.HostingEnvironmentExtensions</c>),
    /// the same helper Umbraco itself uses to know its own backoffice path.
    /// </summary>
    /// <remarks>
    /// Verified against the real Umbraco 17.0.0 binary (decompiled, not
    /// assumed): this resolves <c>hostingEnvironment.ToAbsolute("~/umbraco")</c>,
    /// cached in a static field for the process lifetime. There is no
    /// <c>GlobalSettings.UmbracoPath</c> to read in Umbraco 17 — that property
    /// does not exist on <c>Umbraco.Cms.Core.Configuration.Models.GlobalSettings</c>
    /// any more, so the backoffice path is not independently configurable
    /// through this API today, only through <c>ApplicationVirtualPath</c> if
    /// the whole site is hosted under a different virtual directory. An
    /// earlier draft of this method took a <c>GlobalSettings</c> dependency
    /// on the assumption that it fed this call; it did not compile against
    /// the real package (no such overload exists) and the assumption did not
    /// hold either.
    /// </remarks>
    private bool IsBackofficeRequest()
    {
        var httpContext = ViewContext?.HttpContext;
        if (httpContext is null)
        {
            return false;
        }

        var backOfficePath = _hostingEnvironment.GetBackOfficePath();
        return httpContext.Request.Path.StartsWithSegments(backOfficePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True while a content editor is previewing unpublished content. A
    /// preview session is not the site's audience — see
    /// <see cref="PulseAnalyticsOptions.TrackPreview"/>.
    /// </summary>
    private bool IsPreviewRequest()
    {
        return _umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext)
            && umbracoContext.InPreviewMode;
    }
}
