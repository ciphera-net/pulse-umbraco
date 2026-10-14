using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Ciphera.Pulse.Umbraco;

/// <summary>
/// Registers <see cref="PulseAnalyticsOptions"/> during Umbraco startup.
/// </summary>
/// <remarks>
/// This is the ordinary, documented use of <c>IComposer</c> — the hook
/// Umbraco packages use to register services and configuration, because a
/// package has no <c>Startup</c>/<c>Program.cs</c> of its own to edit (see
/// "Creating a Package" in Umbraco's own docs; every example there registers
/// something through <c>IUmbracoBuilder</c>, never rewrites a response).
/// <c>IComposer</c> implementations in any referenced assembly are
/// discovered and run automatically — installing this package is enough,
/// nothing needs to be called by hand.
///
/// This is deliberately the ONLY thing a composer does in this package.
/// Earlier Pulse integration research flagged "<c>IComposer</c> + ASP.NET
/// middleware rewriting the HTML response" as this platform's weakest
/// technical claim — undocumented, and liable to double-inject or miss a
/// response Umbraco didn't render through the path the middleware expected.
/// This package does not do that: the tag itself is emitted by
/// <see cref="PulseAnalyticsTagHelper"/>, which a developer adds to their own
/// layout, same as every real-world Umbraco analytics/script package found
/// during research (<c>Our.Umbraco.TagHelpers</c>,
/// <c>SeoToolkit.Umbraco</c>'s Script Manager) — a TagHelper the developer
/// places explicitly, not a response rewrite that tries to find
/// <c>&lt;/head&gt;</c> in bytes it does not control the shape of.
/// </remarks>
public sealed class PulseAnalyticsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PulseAnalyticsOptions>, PulseAnalyticsOptionsValidator>());

        builder.Services
            .AddOptions<PulseAnalyticsOptions>()
            .Bind(builder.Config.GetSection(PulseAnalyticsOptions.SectionName))
            .ValidateOnStart();
    }
}
