# Umbraco Marketplace listing — Pulse Analytics

The copy a future session should put into NuGet package metadata (which
IS the listing, for the baseline) and into `umbraco-marketplace.json` if that
file is ever hosted (see RELEASING.md "Open items"). Keep this in step with
the package; it has version control, the NuGet gallery page does not.

**Status:** 🔴 **NOT PUBLISHED.** No nuget.org account has pushed this
package, so there is no listing id, no Marketplace URL and nothing to check
externally yet — unlike the Joomla JED (id `17699`, state `Pending`) or TYPO3
(built, blocked only on an owner-held token), this package has not reached
the point where a store even knows it exists.

## How this listing comes to exist at all

There is no submission form. Publish `Ciphera.Pulse.Umbraco` to nuget.org
with the `umbraco-marketplace` tag and a dependency on `Umbraco.Cms.Core`
(both already set in the csproj), and the Marketplace indexes it within 24
hours, sooner with the forced-sync call in RELEASING.md. The fields below are
therefore not "submission copy" in the Joomla/TYPO3 sense — they are the
**NuGet package metadata**, because that is literally where the baseline
listing's title, description and readme come from.

## Prerequisites

| | State |
|---|---|
| `umbraco-marketplace` NuGet tag | ✅ set in `PackageTags` |
| Direct `Umbraco.Cms.*` dependency (for version-range detection) | ✅ `Umbraco.Cms.Core` |
| Licence | ✅ Apache-2.0 (`PackageLicenseExpression`) — see README.md "Licence" |
| Package pushed to nuget.org | 🔴 not done (RELEASING.md) |
| `PackageIcon` | ✅ `src/Ciphera.Pulse.Umbraco/icon.png`, 128×128 PNG, packed at the `.nupkg` root (RELEASING.md "Open items") |
| `umbraco-marketplace.json` (optional, for Category/screenshots) | 🔴 not shipped for v1, needs a hosted `PackageProjectUrl` first |

## Listing fields

**Package id:** `Ciphera.Pulse.Umbraco`

**Title:** Pulse Analytics for Umbraco

**Short description** (NuGet `Description`, also what a Marketplace card
shows):

> Privacy-first web analytics for Umbraco. No cookies, no personal data, and the script it adds to your pages is under 3 KB.

**Long description** (for the Marketplace page / `umbraco-marketplace-readme.md`
if that file is ever added — until then, the package README is what renders):

Pulse Analytics adds privacy-first web analytics to an Umbraco site with one
NuGet package and a TagHelper you add to your layout. It sets no cookies,
stores no personal data, and the script it adds to your pages is under 3 KB.

How it works

1. `dotnet add package Ciphera.Pulse.Umbraco`, then add
   `@addTagHelper *, Ciphera.Pulse.Umbraco` to `Views/_ViewImports.cshtml`.
2. Add `<pulse-analytics />` inside `<head>` in your layout.
3. Set the domain your site is registered under in Pulse under
   `PulseAnalytics:Domain` in `appsettings.json`, or leave it empty and the
   tracker uses the browser's own hostname — correct for a site served on one
   domain.
4. Your dashboard at pulse.ciphera.net shows visitors as they arrive.

What it measures

Pageviews, referrers, countries, devices, time on page and scroll depth, plus
the goals, funnels and campaigns you set up, in one dashboard hosted in
Switzerland. Visitors whose browser sends Do Not Track or Global Privacy
Control are not counted at all. The backoffice is never tagged, and neither
is a content-preview session unless you turn that on.

Options

A `Companion` setting to also record clicks, copies and form submits, loaded
as a second request so you only pay for it if you want it; and a custom API
origin, if you route events through your own proxy.

You need

Umbraco 17 or later on .NET 10, and a Pulse Analytics account with a site
registered for the same domain. The free plan is enough to start. No database
tables, no extra NuGet dependencies beyond `Umbraco.Cms.Core`.

**Category** (only settable via `umbraco-marketplace.json` — see
RELEASING.md): Analytics & Insights

**Tags:** analytics, privacy, tracking (as a category, not a claim — see
"Copy rules" below), cookieless, GDPR-adjacent terms deliberately avoided (see
below)

**Links:** Website `https://pulse.ciphera.net` · Documentation
`https://docs.ciphera.net/pulse` · Source
`https://github.com/ciphera-net/pulse-umbraco` (does not exist yet — see
RELEASING.md)

**Licence:** Apache License 2.0

## Copy rules this listing follows (and must keep following)

- **Never** "GDPR compliant", "no tracking", or "no consent banner needed" —
  none of those are claims this package or Pulse Analytics makes anywhere
  else, and this listing does not start.
- **Never** a precise script-size figure like "2.7 KB" — the published claim
  everywhere else is "under 3 KB gzipped", and that is the only form used
  here.
- **Product name is always "Pulse Analytics"**, never "Pulse" alone or "Pulse
  by Ciphera" (an earlier, superseded naming decision — see the estate's own
  record of why that changed).

## No Umbraco logo on the listing

No platform logo appears on any Pulse marketplace card across the whole
integration programme — Astro, Nuxt, GTM/Google, Docusaurus/Meta, Drupal,
Joomla, TYPO3 and Odoo were each checked against their own trademark policy
and every one came back "no" (silence on third-party pairing is read as no,
not as permission). Umbraco was not separately re-checked against its own
brand guidelines as part of this task, so treat this the same way by default
— Pulse mark alone, "for Umbraco" in text, until someone reads
`umbraco.com`'s own trademark/brand policy and finds an affirmative grant in
words. This is a NEW platform for the programme's "the logo question,
answered once for all of them" finding, not one of the eight already
checked — carry the same default, don't assume the precedent extends without
reading Umbraco's own policy.

## Screenshots

None produced — there is no running install to screenshot (see RELEASING.md
"What has not been verified against a real install"). Once `scripts/verify.sh`
has run against a real site, the Marketplace convention worth following (same
as the other integrations' listings) is: the settings in `appsettings.json`
shown next to the rendered `<head>` output, and the dashboard at
pulse.ciphera.net showing a pageview arrive from that install — never a
screenshot of someone else's real traffic.
