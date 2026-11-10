# Releasing Ciphera.Pulse.Umbraco

## Status: on GitHub, not yet on nuget.org

The repository is public at `github.com/ciphera-net/pulse-umbraco` (since
14-10-2026; CI since 09-11-2026). Nothing has been published to nuget.org: the
first push waits on an owner-held API key (`NUGET_API_KEY` in the workspace
`.env`). It was first built locally, with no remote and no registry publish,
by explicit instruction while it was written. Everything
below is the documented flow for the **first** real release, written so it
can be followed without re-deriving any of it, and every claim about the
Marketplace's behaviour is sourced from Umbraco's own current documentation
(`docs.umbraco.com/umbraco-dxp/marketplace/listing-your-package`, read
14-10-2026) rather than assumed.

## The surface, and what has NO review gate

Unlike Drupal (no project-approval queue, but a real CI gate once pushed),
Joomla (JED volunteer review, up to 60 days) and TYPO3 (immediate TER
publish, explicitly "everybody can publish an extension"), the **Umbraco
Marketplace has no submission step at all**. Publishing to nuget.org with the
right tag is the whole mechanism:

1. The package must carry the `umbraco-marketplace` tag (already set in
   `Ciphera.Pulse.Umbraco.csproj`'s `PackageTags`) — **only on the installable
   component**. This repo has exactly one package, so that is not a choice to
   get wrong here, but it is the rule to remember if this ever splits into a
   Core/Web pair the way some larger Umbraco packages do.
2. The package must carry a direct NuGet dependency on one of
   `Umbraco.Cms.*` / `UmbracoCms.*` (v8 only) / `Umbraco.Commerce.*` — this
   package depends on `Umbraco.Cms.Core` (see its csproj comment for why that
   one, not `Umbraco.Cms.Web.Common` or the full `Umbraco.Cms` metapackage).
   The Marketplace reads this dependency's version to compute the supported
   Umbraco version range shown on the listing.
3. **Indexing is automatic and asynchronous, not a submission:** a new
   tagged package is picked up by a daily scan at **04:00 UTC**; an
   already-listed package's metadata refreshes every **2 hours**; download
   counts refresh hourly. To force a sync instead of waiting:
   ```bash
   curl -X POST https://functions.marketplace.umbraco.com/api/InitiateSinglePackageSyncFunction \
     -H 'Content-Type: application/json' \
     -d '{"PackageId":"Ciphera.Pulse.Umbraco"}'
   ```
   Rate-limited to one request per minute per package id.
4. There is a **validation tool** at `marketplace.umbraco.com/validate` —
   run the published package through it before announcing the listing
   anywhere, the same spirit as running JED Checker against the published
   Joomla zip rather than trusting the source tree.

## Steps, in order

1. **Pick the version.** First release is `1.0.0`, matching the convention
   every other Pulse integration repo uses for its first tag. There is no
   `<Version>` hardcoded in `Ciphera.Pulse.Umbraco.csproj` on purpose — set
   it at pack time so the one place that knows the real release version is
   the release step itself, not a file that can drift from the tag — a
   different Pulse integration's store listing once sat two releases behind
   its own repo for weeks with nothing watching, precisely because its
   version lived in a file instead of the release step.
   ```bash
   git tag -a v1.0.0 -m "v1.0.0"
   ```
2. **Pack.**
   ```bash
   dotnet pack src/Ciphera.Pulse.Umbraco/Ciphera.Pulse.Umbraco.csproj \
     -c Release -o ./artifacts -p:Version=1.0.0
   ```
   On a machine with no .NET SDK (the workspace Mac has none), run the same
   command in the SDK image the harness uses, from a clean checkout of the tag:
   ```bash
   docker run --rm -v "$PWD":/repo -w /repo mcr.microsoft.com/dotnet/sdk:10.0 \
     dotnet pack src/Ciphera.Pulse.Umbraco/Ciphera.Pulse.Umbraco.csproj \
       -c Release -o /repo/artifacts -p:Version=1.0.0
   ```
3. **Run the real-install harness FIRST — see "What has not been verified
   against a real install" below.** Every sibling Pulse integration's
   RELEASING.md says some version of "validate before you publish, because a
   bad first release costs more than the extra half hour" (`pulse-cli`'s
   README: *"v1.0.0 took four attempts, three of them config/tooling errors
   this would have caught"*). This package has the same risk and, because it
   has never been built or tested on any machine, a materially higher chance
   of a real problem than a package that has at least compiled once.
   ```bash
   ./scripts/verify.sh --mutate
   UMBRACO_TEMPLATE_RANGE='18.*' ./scripts/verify.sh --mutate
   ```
   Run it once per supported major. The package declares
   `Umbraco.Cms.Core >= 17.0.0` with no upper bound, so the Marketplace lists
   it for 17 and every later major; each run prints the versions it resolved
   under "resolved versions", and that line is the evidence, not the template
   range it was asked for.
4. **Push to nuget.org.** Needs an owner-provisioned NuGet.org API key —
   nuget.org lets a key be scoped to "push new packages and package versions"
   for a specific glob (`Ciphera.Pulse.*` or the exact package id), which is
   the scoped-credential shape this estate already uses for
   `DRUPALCODE_TOKEN` and the TYPO3 `TYPO3_API_TOKEN`. This key does not
   exist yet; minting it is an owner action (nuget.org account + API key
   page), not something this session had the credentials to do.
   ```bash
   dotnet nuget push ./artifacts/Ciphera.Pulse.Umbraco.1.0.0.nupkg \
     --api-key "$NUGET_API_KEY" \
     --source https://api.nuget.org/v3/index.json
   ```
   Without a local SDK, pass the key into the container as an environment
   variable and expand it inside the container's shell, so it never appears on
   the host command line:
   ```bash
   NUGET_API_KEY=$(grep '^NUGET_API_KEY=' "<workspace>/.env" | cut -d= -f2- | tr -d '"') \
   docker run --rm -e NUGET_API_KEY -v "$PWD":/repo -w /repo mcr.microsoft.com/dotnet/sdk:10.0 \
     sh -c 'dotnet nuget push artifacts/Ciphera.Pulse.Umbraco.1.0.0.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json'
   ```
   nuget.org has **no human review queue** — the package is live and
   installable the moment the push succeeds. It does run an asynchronous
   malware/security scan afterwards that can flag a package post-publish, but
   that does not block the initial listing, the same "publish now, review
   later" shape as TYPO3's TER.
5. **Confirm the Marketplace picked it up.** Either wait for the next 04:00
   UTC scan or force it with the sync endpoint above, then check
   `marketplace.umbraco.com` for the listing and run the validator
   (`marketplace.umbraco.com/validate`) against it.
6. ✅ **The GitHub repo the metadata points at exists** (public since
   14-10-2026), so `PackageProjectUrl` and `RepositoryUrl` in
   `Directory.Build.props` resolve.
7. **Watch the listing.** Add a `nuget` listing to `marketplace-version-check`
   (the Infra watcher that already compares wordpress.org, npm, Packagist,
   drupal.org, the Joomla manifest and apps.odoo.com against each repo's
   newest tag), so a release that never reaches nuget.org raises an alert.
8. **GitHub Release notes for the tag** (`gh release create v1.0.0`), as for
   every tag on a public package.

## Open items — things this repository could not settle itself

- **CI since 09-11-2026:** Woodpecker runs the security scan and `dotnet test`
  (in the mirrored .NET 10 SDK image) on every pull request and push to
  `main`; `scripts/verify.sh`, a real Umbraco install, stays local. The
  version-drift watcher for nuget.org is step 7 above; it can only be added
  once the package exists there.
- **The package icon is set. Only the first push shows how nuget.org and the
  Marketplace render it.** `src/Ciphera.Pulse.Umbraco/icon.png` is a 128×128
  PNG (1,238 bytes, the Pulse mark in white layers on the brand orange
  `#FD5E0F`), copied byte for byte from the brand library's v12 Umbraco icon,
  SHA-256 `0df8ac7d61689c71bdb0ab7716553bf0970b7578a9980afab8a9522a3c01ceb9`.
  Version 1.0.0 shipped the earlier dark icon; 1.0.1 carries this one.
  The csproj sets `<PackageIcon>icon.png</PackageIcon>` and packs the file
  with `<None Include="icon.png" Pack="true" PackagePath="\" />`.
  `dotnet pack` puts it at the root of the `.nupkg` with that same hash and
  writes `<icon>icon.png</icon>` into the nuspec (measured 19-10-2026).
  NuGet takes a PNG or JPEG up to 1 MB and recommends 128×128. The
  Marketplace reads the icon from the NuGet metadata, and
  `umbraco-marketplace.json` has no icon field, so there is nothing to host.
  After the push, check the icon on the nuget.org page and on the Marketplace
  listing. The icon is the Pulse mark alone, with no platform logo (see "No
  Umbraco logo on the listing" in LISTING.md).
- **`umbraco-marketplace.json` was deliberately not shipped for v1.** It is
  optional ("You may choose to add...") and must be hosted at the literal
  `PackageProjectUrl` — which here is the GitHub repo, not a domain Pulse
  serves, so the file would 404 if added today. The base listing (title,
  description, readme, tags, icon) comes from NuGet metadata
  alone and needs nothing hosted. If deeper customisation is wanted later —
  a `Category` of "Analytics & Insights" in particular, which the baseline
  listing will NOT carry without this file — either host
  `pulse.ciphera.net/umbraco-marketplace.json` and repoint
  `PackageProjectUrl` there, or confirm the Marketplace will read it from a
  GitHub raw path (unverified either way).
- **Dual-registry publishing has no established NuGet shape in this estate
  yet.** Every public *npm* Pulse package publishes to both npmjs and GitHub
  Packages (`~/.npmrc` maps the `@ciphera-net` scope); there is no
  equivalent "always do both" rule written down for NuGet, and this is the
  estate's first NuGet package. Publishing to GitHub Packages' NuGet feed as
  well as nuget.org is possible but not assumed here — an owner decision,
  not inferred from the npm convention.
- **CI and tracking exist since 09-11-2026** (Woodpecker; the NuGet release is
  tracked with the other store listings).

## ✅ Verified against a real install (14-10-2026, re-run 09-11-2026)

**Re-run 09-11-2026** on Umbraco 17.8.1 and 18.3.1 (templates of the same
versions, .NET SDK 10.0.403), plain and `--mutate`: all green, with the
resolved `Umbraco.Cms.Core` printed by the harness (17.8.1 and 18.3.1). Two
harness changes came out of it, each proven with a control:

- **It names the versions it ran on** ("resolved versions", from
  `dotnet list package --include-transitive`), and fails when that line lacks
  `Umbraco.Cms.Core` or this package. Control: a copy whose filter drops this
  package failed that one check and no other.
- **A failed setup step stops the run.** One run hit a DNS failure inside the
  container (`Name or service not known (api.nuget.org:443)`) at the pack
  step, carried on without the package, and failed five later checks with
  messages about the tag. Control: a template range that does not exist
  (`99.*`) now stops at the template step with
  `REFUSING: setup failed (install the Umbraco templates, exit 103)`.

The 14-10-2026 account follows.

`scripts/verify.sh` has now run, for real, inside Docker (`mcr.microsoft.com/dotnet/sdk:10.0`,
Umbraco 17.7.1, .NET 10.0.402) — both the plain run and `--mutate`, both green. This package has
compiled, been unit-tested, packed, installed into a real scaffolded Umbraco site, and served the
real tag over HTTP. **Nothing below is "reasoned" any more; everything is measured.**

- **`dotnet build` / `dotnet test` / `dotnet pack` all run clean.** 40/40 unit tests pass
  (`TagBuilderTests`, `PulseAnalyticsOptionsValidatorTests`, `PulseAnalyticsTagHelperTests`).
- **The harness itself had never been run and had eight first-run defects**, every one found by
  running it for real and fixed with a control proving where the fault actually was before
  touching any code (see the git log — each fix is its own commit): two compile errors in the
  package itself (a missing `using` for `IUmbracoBuilder`; a nonexistent
  `GlobalSettings.GetBackOfficePath(IHostingEnvironment)` overload — the real API is
  `IHostingEnvironment.GetBackOfficePath()`, confirmed by decompiling the real 17.0.0 binary), and
  six harness-only defects: `dotnet pack` against a `:ro` mount, missing scaffold directories, a
  `SurfaceController` fixture with no constructor, a self-killing `pkill -f` pattern that also
  never matched its real target, `--no-launch-profile` silently dropping
  `ASPNETCORE_ENVIRONMENT=Development` (so every check would have silently exercised Umbraco's own
  "No Published Content" shell, never this package's code), a missing route attribute + wrong
  layout path once the site was actually reachable, and a corrupted `_ViewImports.cshtml` (a
  missing trailing newline turned two `@addTagHelper` directives into one run-on string — `dotnet
  build` never caught it, Umbraco's own runtime view compiler failed every single request on it).
- **`PulseAnalyticsTagHelperTests`' backoffice-path assertion was re-derived, not merely
  confirmed**: `IHostingEnvironment.GetBackOfficePath()` resolves `ToAbsolute("~/umbraco")` — a
  literal, not a read of `GlobalSettings.UmbracoPath` (that property does not exist in Umbraco 17
  any more). The real-install surface-controller check proves the end-to-end behaviour regardless
  of this detail.
- **One harness check was removed, not fixed, for a real architectural reason**: "an invalid
  domain is dropped, never injected" can never be observed on a config-bound real site —
  `PulseAnalyticsOptionsValidator` runs the identical `IsValidDomain` check with
  `ValidateOnStart()`, so the host throws `OptionsValidationException` and exits before Kestrel
  binds a port. `TagBuilderTests` already proves `BuildScriptsHtml`'s own defensive drop in
  isolation; the real-install equivalent that matters is the next check down
  ("refuses to start the site, loudly"), which is what actually happens.
- **The `Umbraco.Cms.Core 17.0.0` dependency installs and resolves cleanly** against a real
  Umbraco 17.7.1 site (the templates' current version as of 14-10-2026).
- **The NuGet.org publish flow itself remains unexercised** — no account, no API key, no prior
  push from this estate to compare against. That is the one claim in this file still unverified.
