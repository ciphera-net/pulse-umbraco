# Releasing Ciphera.Pulse.Umbraco

## Status: not yet published, not yet pushed anywhere

This repository was built locally, with no remote, no GitHub repo, and no
registry publish — by explicit instruction while it was written. Everything
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
   ```
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
   nuget.org has **no human review queue** — the package is live and
   installable the moment the push succeeds. It does run an asynchronous
   malware/security scan afterwards that can flag a package post-publish, but
   that does not block the initial listing, the same "publish now, review
   later" shape as TYPO3's TER.
5. **Confirm the Marketplace picked it up.** Either wait for the next 04:00
   UTC scan or force it with the sync endpoint above, then check
   `marketplace.umbraco.com` for the listing and run the validator
   (`marketplace.umbraco.com/validate`) against it.
6. **Push the GitHub repo this metadata already assumes exists.**
   `PackageProjectUrl`/`RepositoryUrl` in `Directory.Build.props` point at
   `https://github.com/ciphera-net/pulse-umbraco` — that repository does not
   exist yet anywhere; creating it (and pushing this history to it) is a
   prerequisite for those links to resolve, and is explicitly out of scope
   for the task that produced this repo (no remote, no `gh repo create`, no
   push). Do this before or in the same step as the nuget.org push, not
   after — a listing linking to a 404 is its own small defect.

## Open items — things this repository could not settle itself

- **No CI exists for this repo yet** — it has never been pushed anywhere for
  a CI system to register. Before relying on this package long-term, add a
  pipeline step that runs `dotnet test` and `scripts/verify.sh` inside a
  `mcr.microsoft.com/dotnet/sdk` image, the same way the other integrations'
  `scripts/check.sh`/`scripts/verify.sh` are wired into their own CI — and,
  per the lesson above, add a version-drift watcher comparing the nuget.org
  version to the latest git tag, since NOTHING else will notice if a future
  release fails to publish.
- **No PackageIcon is set.** The Marketplace pulls the icon from NuGet
  package metadata; without one the listing shows nuget.org's generic
  default. Add a 128×128 PNG via `<PackageIcon>icon.png</PackageIcon>` plus
  an `<None Include="icon.png" Pack="true" PackagePath="\" />` before the
  real release — this needs a small design pass this task was not scoped to
  do (Pulse mark alone; no platform logo — see "No Umbraco logo on the
  listing" below).
- **`umbraco-marketplace.json` was deliberately not shipped for v1.** It is
  optional ("You may choose to add...") and must be hosted at the literal
  `PackageProjectUrl` — which here is the GitHub repo, not a domain Pulse
  serves, so the file would 404 if added today. The base listing (title,
  description, readme, tags, icon once added) comes from NuGet metadata
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
- **No CI registration, no work-tracking entry.** This task's scope note
  explicitly forbade posting anywhere, so none was filed. The first session
  that pushes this repo and sets up CI should do both.

## ✅ Verified against a real install (14-10-2026)

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
