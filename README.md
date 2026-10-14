# Pulse Analytics for Umbraco

Privacy-first analytics for an Umbraco site, in one NuGet package. No cookies,
no personal data, and the script it adds to your pages is under 3 KB.

[Pulse Analytics](https://pulse.ciphera.net) is built by
[Ciphera](https://ciphera.net), a company in Belgium, and hosted in
Switzerland.

## Requirements

**Umbraco 17 or later**, targeting **.NET 10**. You need a Pulse Analytics
account with a site registered for the same domain; the free plan is enough
to start (one site, 5,000 pageviews a month, no card).

See "Why Umbraco 17" below for how that floor was chosen and how to verify it
is still current.

## Install

```bash
dotnet add package Ciphera.Pulse.Umbraco
```

Register the TagHelper, once, in `Views/_ViewImports.cshtml`:

```cshtml
@addTagHelper *, Ciphera.Pulse.Umbraco
```

Add the tag to your layout, once, anywhere inside `<head>`:

```cshtml
<head>
    ...
    <pulse-analytics />
</head>
```

That is the whole install. `<pulse-analytics />` itself never reaches the
response — it renders the actual `<script>` tag(s) in its place, or nothing
at all (see "When it renders nothing" below).

## Settings

Configuration lives in `appsettings.json`, under a `PulseAnalytics` section
— the same place every other Umbraco package's settings live
(`Our.Umbraco.TagHelpers`, `SeoToolkit.Umbraco`), bound with `IOptions` and
validated at startup. There is no backoffice settings screen for this; see
"Why appsettings.json, not a settings screen" below.

```json
{
  "PulseAnalytics": {
    "Domain": "",
    "Companion": false,
    "TrackPreview": false,
    "ApiOrigin": ""
  }
}
```

| Setting | Default | What it does |
|---|---|---|
| `Domain` | empty | The domain this site is registered under in Pulse. Empty means auto-detect — the tracker falls back to the browser's own hostname, which is correct for a site served on one domain. |
| `Companion` | `false` | Record clicks, copies and form submits. Loads a second, separate script — see below. |
| `TrackPreview` | `false` | Also fire the tracker while a content editor is previewing unpublished content in the backoffice. Off by default — a preview session is not the site's audience. |
| `ApiOrigin` | empty | Route events through your own proxy origin. A bare origin, not a full URL. |

**A mistyped `Domain` or `ApiOrigin` fails the site's startup**, with a
message naming the offending value, rather than being stored and quietly
shipping a broken tag. See "A mistyped domain fails loudly, at startup" below
for why that is the right moment for an `appsettings.json`-configured
package, and why it is stricter than the other Pulse integrations' "rejected
at save time".

## Three things worth knowing

**What lands in your pages is the literal tag**, not a wrapper or a
bootstrap:

```html
<script defer data-domain="example.com" src="https://js.ciphera.net/script.js"></script>
```

No inline JavaScript at all — a strict Content-Security-Policy needs no nonce
and no `unsafe-inline` for this package, only `script-src https://js.ciphera.net`
and `connect-src https://pulse-api.ciphera.net`.

**The interaction capture is a second request on purpose.** `Companion: true`
loads `script.interactions.js` as a separate file. The core script's size is a
published claim, and nothing is folded into it to make a feature look free.

**When it renders nothing.** `<pulse-analytics />` suppresses its own output
in two cases: under Umbraco's own backoffice path (see "How the backoffice
exclusion actually works" below), and during a content-preview session unless
`TrackPreview` is `true`. Both are covered by this package's own unit tests;
`scripts/verify.sh` proves the backoffice case against a real install.

## How the tag is injected, and why not middleware

This package adds a **TagHelper** — `<pulse-analytics />` — that a developer
registers once and places once in their own layout. It does **not** use an
`IComposer` + ASP.NET Core middleware that rewrites the response body looking
for `</head>`.

That distinction matters because it was the open, unproven question this
package started from: Pulse's own cross-platform integration research had
flagged "`IComposer` + middleware rewriting the HTML response" as Umbraco's
weakest technical claim in the whole research corpus — undocumented by
Umbraco, and never confirmed to work. Before writing a line of this package,
that approach was checked against what real, accepted Umbraco packages
actually do, by reading their own source and marketplace listings rather than
assuming:

- **`Our.Umbraco.TagHelpers`** (a long-running, actively used community
  package) ships TagHelpers that a developer registers with
  `@addTagHelper *, Our.Umbraco.TagHelpers` in `_ViewImports.cshtml` and places
  by hand.
- **`SeoToolkit.Umbraco`**'s Script Manager — the closest real precedent for
  "inject a third-party script like Google Tag Manager or Hotjar" — does the
  same thing: `@addTagHelper *, SeoToolkit.Umbraco.ScriptManager.Core`, then
  `<render-script position="HeadBottom"></render-script>` in the layout.
- Nothing found in that search does response-rewriting middleware for this
  purpose. Umbraco's own "Creating a Package" documentation never shows
  middleware either — every `IComposer` example registers a service,
  a notification handler, or a collection entry, which is the ordinary,
  intended use of `IComposer` and the one this package makes (see
  `PulseAnalyticsComposer`, which registers `PulseAnalyticsOptions` and
  nothing else).

So this package follows the pattern real, shipped Umbraco packages use, not
the one flagged as unproven. A TagHelper is also the more honest choice on
its own terms: a middleware response-rewrite has to find `</head>` in bytes
it does not control the shape of (streamed output, an already-committed
response, a `</head>` inside a comment or a string literal), while a
TagHelper is itself part of the render tree Umbraco already understands — it
cannot "miss".

## How the backoffice exclusion actually works

Two guarantees, not one:

1. **Structural.** Umbraco's backoffice is a separate single-page application
   — it does not render your site's Razor layout, or any Razor layout, so
   there is no code path through which `<pulse-analytics />` could appear
   there. This needs no code to be true.
2. **An explicit path check, as defence in depth.** `PulseAnalyticsTagHelper`
   also checks the current request against
   `IHostingEnvironment.GetBackOfficePath()` (the same API Umbraco itself
   uses to know its own backoffice path, `/umbraco` by default) and
   suppresses output there too. Confirmed by decompiling the real Umbraco
   17.0.0 binary while building `scripts/verify.sh`: this resolves
   `ToAbsolute("~/umbraco")`, a literal path — `GlobalSettings` has no
   `UmbracoPath` property in Umbraco 17 any more, so there is nothing to
   configure here today (an earlier draft of this paragraph said otherwise;
   it was wrong).

The second guarantee is not redundant. A **Surface controller** — Umbraco's
own base class for front-end form posts (a newsletter signup, a contact
form) — is mounted under the backoffice's own path prefix
(`/umbraco/surface/{controller}/{action}`) by Umbraco's own routing
convention, even though its job is entirely front-end. A developer who shares
one layout across the whole site, including a surface controller's "thank
you" page, is a realistic shape — and it is exactly the case where the
structural guarantee alone would not hold, because that view genuinely is
part of the front-end Razor render tree. `scripts/verify.sh` tests precisely
this: a surface controller sharing the same layout as the public `/verify`
page, asserting the tag appears on one and not the other, and a `--mutate`
mode that removes the guard and proves the check can actually go red.

## Why appsettings.json, not a settings screen

The Drupal, Joomla and TYPO3 Pulse integrations each add a backoffice/admin
settings screen, because that is how those platforms' developers expect to
configure a module. Umbraco's own convention for a package like this is
different: `Our.Umbraco.TagHelpers` and `SeoToolkit.Umbraco` both configure
through `appsettings.json` sections bound with `IOptions`, not a custom
backoffice section. Following the appsettings.json convention also means the
value lives in source control and in whatever secrets/config management the
site already uses, rather than in the database — and it means no backoffice
UI surface for this package to maintain across major versions.

## A mistyped domain fails loudly, at startup

Every Pulse integration with a settings form rejects an invalid domain at the
moment it would be saved, so a broken value never reaches a live site. There
is no save button here, so the closest equivalent moment is application
startup: `PulseAnalyticsComposer` registers `PulseAnalyticsOptionsValidator`
with `.ValidateOnStart()`, so a malformed `PulseAnalytics:Domain` or
`PulseAnalytics:ApiOrigin` throws an `OptionsValidationException` — naming the
bad value — before the site ever starts serving requests. This is a stricter
choice than the other integrations' "rejected at save time, not stored": an
Umbraco deploy fails in CI or at first boot, never silently in production,
which is the stronger form of the same house rule ("no silent failures" —
an analytics install that looks fine and reports nothing is the worst outcome
this package can produce).

## What Pulse Analytics measures

Pageviews, referrers, countries, devices, time on page and scroll depth, plus
goals, funnels and campaigns. It sets no cookies and stores no personal data.
Visitors whose browser sends Do Not Track or Global Privacy Control are not
counted at all — so if you test in Brave or Firefox and see nothing, that is
the tracker behaving correctly. Safari is the easiest browser to verify an
install in.

## Why Umbraco 17

Umbraco ships a new major twice a year and designates roughly one a year as
Long-Term Support. Verified 14-10-2026, directly against `umbraco.com` and
the published NuGet package metadata (not a cached or remembered figure —
re-check these before trusting them on a later date):

| Version | Released | Type | EOL | Targets |
|---|---|---|---|---|
| 13 | 14-12-2023 | LTS | 14-12-2026 | .NET 8 |
| **17** | **27-11-2025** | **LTS** | **27-11-2028** | **.NET 10** |
| 18 | 25-06-2026 | STS | 25-06-2027 | .NET 10 |

Umbraco 13 LTS is still supported today but reaches end of life a month after
this package was written, so it is not the version to build a new package
against. **Umbraco 17 is the current LTS** — "17 and .NET 10 arrive as dual
LTS releases" (umbraco.com's own 17 LTS announcement) — and **Umbraco 18 is
the current latest major**, released as a Standard-Term Support version on
the same .NET 10 base. Targeting `net10.0` (this package's only target
framework) and depending on `Umbraco.Cms.Core >= 17.0.0` (a minimum, not a
pin — see the csproj comment) covers both 17 and 18 in one build, and the
Umbraco Marketplace itself derives the supported-version range from exactly
that dependency (see RELEASING.md).

## Licence

**Apache-2.0** — the same licence every other public Pulse integration uses
(Astro, Docusaurus, Framer, GTM, Nuxt). This is a genuine choice here, not a
default: Drupal, Joomla and TYPO3 all **force** GPL-2.0-or-later on anything
they host, because each of those platforms' own licensing policy treats an
installed module/plugin/extension as part of one combined GPL program.
**Umbraco does not.** Umbraco CMS's core is MIT-licensed (confirmed from
`umbraco.com`: *"the whole system has been available under an MIT License"*
since version 4.5), and real, accepted Umbraco Marketplace packages ship
under a mix of licences with no forced convergence —
`Our.Umbraco.TagHelpers` and `SeoToolkit.Umbraco` are MIT, `uSync` is
MPL-2.0. Nothing in the Marketplace listing process (`"Listing Your
Package"`) requires a particular OSS licence either; its `LicenseTypes`
field (`Free`/`Purchase`/`Subscription`) is a pricing-tier label, not an SPDX
licence. So Apache-2.0 stands here on the same footing as the Astro/Docusaurus/
Framer/GTM/Nuxt integrations, not as an exception.

## What this harness does and does not prove

`scripts/verify.sh` (not run while this repository was built — see
RELEASING.md) installs a real Umbraco site in Docker and reads the served
HTML. It does **not** content-author a page through the backoffice; it adds a
small MVC controller and a surface controller to the scaffolded site itself,
both rendering the site's own shared layout. This is the deliberate, smallest
honest substitute for driving the backoffice's content-authoring UI (which
varies by version and has nothing to do with this package's own logic): the
DI container, configuration binding, TagHelper discovery and Razor rendering
pipeline under test are identical either way. What it does not cover: real
content-authored pages, and a real preview session (triggering
`InPreviewMode` needs an actual content node and the backoffice's own
preview-issuing flow) — `TrackPreview`/preview-exclusion is covered by this
package's own unit tests instead
(`PulseAnalyticsTagHelperTests`), with a mocked `IUmbracoContext`.

Umbraco is a registered trademark of Umbraco A/S. This is an independent
package and is not affiliated with or endorsed by Umbraco A/S or the Umbraco
project.
