#!/usr/bin/env bash
# Install a real Umbraco site, add this package, and read the tag out of the
# served HTML.
#
# 🔴 THIS IS THE CHECK THAT MATTERS, and reading the code is not a substitute
# — see the sibling Pulse integrations' own scripts/verify.sh for the house
# rule this one follows: Joomla's harness would have shipped
# `script.js?9b26ab` (a cache-busting query the code never asked for) if
# nothing had read the actual response bytes.
#
# UNLIKE the Drupal/Joomla/TYPO3 verify harnesses, this one needs Docker,
# because there is no .NET SDK on the machine this was written on at all —
# not an old version, none. Every step below runs inside ONE long-lived
# container via `docker exec`, so there is nothing to install on the host.
#
#   ./scripts/verify.sh            # the full real-install check
#   ./scripts/verify.sh --mutate   # ALSO proves the backoffice guard can fail
#   ./scripts/verify.sh --keep     # skip teardown, for debugging
#
# Needs `docker` on PATH and network access (NuGet restore, the Umbraco
# templates, the .NET SDK image). Exits non-zero if any case does not match.
#
# 🟢 RUN FOR REAL 14-10-2026, both plain and --mutate, both green — see
# RELEASING.md "Verified against a real install" for the full account,
# including the eight first-run defects (two in the package, six in this
# script) found and fixed getting here, each its own git commit. It was
# NOT run in the task that originally wrote it (Docker and dotnet were both
# off-limits then); that history is why it needed a first real run at all.
# See README.md "What this harness does and does not prove" for what is
# still out of scope (real content-authored pages, a real preview session).
#
# Every container, volume and network this script creates is named
# pulse-verify-umbraco-*, and teardown removes ONLY resources matching that
# name — never a prune, matching the house rule for Docker hygiene.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFIX="pulse-verify-umbraco"
NETWORK="${PREFIX}-net"
NUGET_VOLUME="${PREFIX}-nuget"
CONTAINER="${PREFIX}-sdk"
SDK_IMAGE="${SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0}"
UMBRACO_TEMPLATE_RANGE="${UMBRACO_TEMPLATE_RANGE:-17.*}"
# A fixed, clearly-fake version for the local feed only. The real publish
# version lives in RELEASING.md / the release tag, never here — this number
# is never seen by anyone installing the real package.
VERIFY_VERSION="0.0.1-verify"
ADMIN_PASS="fixture-$(LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 24)"

MUTATE=0
KEEP=0
for arg in "$@"; do
  case "$arg" in
    --mutate) MUTATE=1 ;;
    --keep) KEEP=1 ;;
    *) echo "unknown argument: $arg" >&2; exit 1 ;;
  esac
done

command -v docker >/dev/null || { echo "REFUSING: docker is not on PATH"; exit 1; }

FAILURES=0

# ── Teardown: ONLY pulse-verify-umbraco-* resources, by name, never a prune ──
teardown() {
  if [ "$KEEP" -eq 1 ]; then
    echo "==> --keep set: leaving $CONTAINER, $NETWORK, $NUGET_VOLUME running"
    return
  fi
  echo "==> tearing down ${PREFIX}-* resources"
  docker ps -a --filter "name=^/${PREFIX}-" --format '{{.Names}}' | while read -r name; do
    docker rm -f "$name" >/dev/null 2>&1 && echo "    removed container $name"
  done
  docker volume ls --filter "name=^${PREFIX}-" --format '{{.Name}}' | while read -r name; do
    docker volume rm "$name" >/dev/null 2>&1 && echo "    removed volume $name"
  done
  docker network ls --filter "name=^${PREFIX}-" --format '{{.Name}}' | while read -r name; do
    docker network rm "$name" >/dev/null 2>&1 && echo "    removed network $name"
  done
}
trap teardown EXIT

run() { docker exec "$CONTAINER" bash -lc "$1"; }

# Kills a previous run of the scaffolded site, if one is still up.
#
# 🔴 Two real bugs found running this for the first time, both load-bearing:
# 1. The running process is the self-contained apphost `PulseVerifySite`
#    (no `.dll`) — `dotnet run` execs it directly as a child, it is never
#    invoked as `dotnet exec PulseVerifySite.dll`. A pattern ending in
#    ".dll" never matches it at all.
# 2. `pkill -f PATTERN` matches a process's FULL COMMAND LINE. If PATTERN is
#    written literally inside the `bash -lc "...pkill -f PATTERN..."` string
#    passed to this same `docker exec`, that invoking bash -lc process's own
#    argv contains PATTERN too — so pkill matches and kills ITS OWN PARENT
#    SHELL before it can do anything else, and every statement after it in
#    that one-liner (the "sleep 1" grace period) never runs. Passing the
#    pattern through an environment variable instead keeps it out of the
#    argv `pkill -f` reads, since environ is a different thing `-f` does not
#    search — confirmed by running each form directly: inlined, the
#    invoking shell was killed (exit 143, SIGTERM) before printing anything
#    after the pkill; via env var, it printed normally and the signal still
#    reached the real target.
kill_site() {
  docker exec -e KILL_PATTERN=PulseVerifySite "$CONTAINER" bash -lc 'pkill -f "$KILL_PATTERN" 2>/dev/null; sleep 1; true'
}

# ── Bring the SDK container up ───────────────────────────────────────────────
echo "==> creating $NETWORK, $NUGET_VOLUME, $CONTAINER"
docker network create "$NETWORK" >/dev/null
docker volume create "$NUGET_VOLUME" >/dev/null
docker run -d --name "$CONTAINER" --network "$NETWORK" \
  -v "$NUGET_VOLUME":/root/.nuget/packages \
  -v "$REPO_ROOT":/repo:ro \
  "$SDK_IMAGE" sleep infinity >/dev/null

run "mkdir -p /work"

# ── Build our own package from the mounted (read-only) repo ────────────────
# /repo is mounted :ro, so the project's own obj/bin cannot be used for
# intermediate/output files — MSBuild writes there by default regardless of
# -o (which only controls the final .nupkg's destination). Redirect both to
# a writable path under /work, or `dotnet pack` fails with "Read-only file
# system" on the very first run against a never-built repo.
echo "==> packing Ciphera.Pulse.Umbraco $VERIFY_VERSION from /repo"
run "dotnet pack /repo/src/Ciphera.Pulse.Umbraco/Ciphera.Pulse.Umbraco.csproj \
      -c Release -o /work/local-nupkgs -p:Version=$VERIFY_VERSION \
      -p:BaseIntermediateOutputPath=/work/obj-pack/ -p:BaseOutputPath=/work/bin-pack/"

# ── Scaffold a throwaway Umbraco site ────────────────────────────────────────
echo "==> installing the Umbraco $UMBRACO_TEMPLATE_RANGE templates"
run "dotnet new install Umbraco.Templates::$UMBRACO_TEMPLATE_RANGE"

echo "==> scaffolding an unattended Umbraco site (SQLite, no external DB needed)"
run "cd /work && dotnet new umbraco -n PulseVerifySite \
      --development-database-type SQLite \
      --friendly-name 'Pulse Verify' --email admin@example.com --password '$ADMIN_PASS'"
run "mv /work/PulseVerifySite /work/site"

cat > /tmp/pulse-verify-nuget.config <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="pulse-local" value="/work/local-nupkgs" />
  </packageSources>
</configuration>
EOF
docker cp /tmp/pulse-verify-nuget.config "$CONTAINER":/work/site/NuGet.config

# ── A page that WOULD carry the tag, without the integration yet ────────────
# This is the "smallest honest alternative" to content-authoring a real
# Umbraco page through the backoffice: a plain MVC controller/view in the
# same project, sharing one layout. It exercises the SAME DI container,
# configuration binding and Razor/TagHelper discovery a real content page
# would — what is NOT under test here is Umbraco's own content-rendering
# pipeline, which has nothing to do with this package. See README.md
# "What this harness does and does not prove".
#
# Two versions of the layout are prepared up front, written whole rather
# than patched in place, so there is no in-container sed/python text
# surgery to get subtly wrong: "before" (no <pulse-analytics/> at all, the
# negative control) and "after" (the tag before </head>, once the package
# is actually referenced).
mkdir -p /tmp/pulse-verify-site/Controllers /tmp/pulse-verify-site/Views/Verify /tmp/pulse-verify-site/Views/Shared

cat > /tmp/pulse-verify-site/Controllers/VerifyController.cs <<'EOF'
using Microsoft.AspNetCore.Mvc;

namespace PulseVerifySite.Controllers;

// A plain front-end page. Reachable at /verify.
//
// [Route] is NOT decoration here — it is load-bearing. Umbraco's own
// content-first dynamic route (a catch-all "{**slug}" matched by
// UmbracoRouteValueTransformer) sits ahead of plain conventional MVC
// routing, and this scaffolded site has zero published content. Without an
// explicit, more-specific route, "/verify" fell through to THAT and Umbraco
// served its own 200-OK "No Published Content"/installer page — this
// controller was simply never reached, confirmed by comparing the response
// body before and after adding the attribute.
[Route("verify")]
public class VerifyController : Controller
{
    public IActionResult Index() => View("~/Views/Verify/Index.cshtml");
}
EOF

cat > /tmp/pulse-verify-site/Controllers/VerifySurfaceController.cs <<'EOF'
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;

namespace PulseVerifySite.Controllers;

// A real Umbraco SurfaceController — the base class used for front-end
// form posts (newsletter signups, contact forms) — mounted under Umbraco's
// OWN backoffice path prefix by Umbraco's own routing convention
// (/umbraco/surface/{controller}/{action}), not a path this package
// invented. It renders the SAME shared layout as /verify on purpose: a
// developer who shares one layout across the whole site, including a
// surface-controller "thank you" page, is a realistic shape, and it is
// exactly the case the explicit backoffice-path check in
// PulseAnalyticsTagHelper exists for — the structural "the backoffice
// never renders this" argument does not cover a surface controller, because
// a surface controller's view CAN be the site's own layout.
//
// SurfaceController has no parameterless constructor — every real-world
// subclass takes these six services and forwards them to the base
// constructor; ASP.NET Core's own DI container supplies them all. Confirmed
// against the real Umbraco 17.7.1 binary (decompiled), not assumed.
public class VerifySurfaceController : SurfaceController
{
    public VerifySurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
    }

    public IActionResult Index() => View("~/Views/Verify/Index.cshtml");
}
EOF

cat > /tmp/pulse-verify-site/Views/Verify/Index.cshtml <<'EOF'
@{
    // An absolute path, not the bare "_Layout.cshtml": Umbraco's host does
    // not extend the default Razor view-location search with
    // "Views/Shared/{0}.cshtml" the way a stock MVC template's
    // _ViewStart.cshtml convention would — confirmed by the real error this
    // produced first: "The layout view '_Layout.cshtml' could not be
    // located. The following locations were searched: /Views/Verify/_Layout.cshtml"
    // (exactly one location, not the usual two-or-more). The exact same
    // reasoning Index.cshtml's own View("~/Views/Verify/Index.cshtml") call
    // already uses.
    Layout = "~/Views/Shared/_Layout.cshtml";
}
<p>pulse-verify</p>
EOF

cat > /tmp/pulse-verify-site/Views/Shared/_Layout.before.cshtml <<'EOF'
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>pulse-verify</title>
</head>
<body>
    @RenderBody()
</body>
</html>
EOF

cat > /tmp/pulse-verify-site/Views/Shared/_Layout.after.cshtml <<'EOF'
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>pulse-verify</title>
    <pulse-analytics />
</head>
<body>
    @RenderBody()
</body>
</html>
EOF

# The scaffolded Umbraco 17 site ships NEITHER Controllers/ NOR
# Views/Shared/ out of the box (its default template has only Views/,
# Views/Partials/* and _ViewImports.cshtml — a content-first CMS has no
# reason to pre-create a plain-MVC controllers folder). `docker cp` refuses
# to create missing destination directories, so create them first.
run "mkdir -p /work/site/Controllers /work/site/Views/Shared /work/site/Views/Verify"
docker cp /tmp/pulse-verify-site/Controllers/. "$CONTAINER":/work/site/Controllers/
docker cp /tmp/pulse-verify-site/Views/Verify/. "$CONTAINER":/work/site/Views/Verify/
docker cp /tmp/pulse-verify-site/Views/Shared/_Layout.before.cshtml "$CONTAINER":/work/site/Views/Shared/_Layout.before.cshtml
docker cp /tmp/pulse-verify-site/Views/Shared/_Layout.after.cshtml "$CONTAINER":/work/site/Views/Shared/_Layout.after.cshtml

# ── Negative control: the page exists, the integration does not ─────────────
run "cp /work/site/Views/Shared/_Layout.before.cshtml /work/site/Views/Shared/_Layout.cshtml"

start_site() {
  kill_site
  # ASPNETCORE_ENVIRONMENT=Development is NOT optional here. `--no-launch-profile`
  # means launchSettings.json's own "ASPNETCORE_ENVIRONMENT": "Development" never
  # applies, so the default is Production — which never layers
  # appsettings.Development.json on top of appsettings.json. That file is where
  # the scaffold's unattended-install settings AND its SQLite connection string
  # live (appsettings.json alone has neither). Confirmed directly: without this,
  # the site still answers 200 on every path, but it is Umbraco's OWN
  # "No Published Content" / installer shell, never this project's code — a
  # silent false pass this check() would never have caught, since curl -f only
  # treats >=400 as failure, and 200 is 200 whoever served it.
  local env_args=(-e "ASPNETCORE_ENVIRONMENT=Development")
  for kv in "$@"; do env_args+=(-e "$kv"); done
  docker exec -d -w /work/site "${env_args[@]}" "$CONTAINER" bash -lc \
    "exec dotnet run --no-launch-profile --urls http://127.0.0.1:5000 > /work/site.log 2>&1"
  # The FIRST boot of a fresh Umbraco site also runs the unattended install
  # (schema + migrations against the SQLite file), which is slower than a
  # warm restart — budget three minutes, not the Joomla harness's ten
  # seconds, and let a real run correct this number if it is still wrong.
  for _ in $(seq 1 180); do
    run "curl -sf -o /dev/null http://127.0.0.1:5000/verify" && return 0
    sleep 1
  done
  return 1
}

# $1 label, $2 "ENV=val ENV2=val2" (may be empty string), $3 must-match ERE, $4 must-NOT-match ERE (may be empty)
check() {
  local html html_flat
  if ! start_site $2; then
    echo "    ❌ $1 — site did not become ready"
    echo "       last 40 lines of /work/site.log:"
    run "tail -n 40 /work/site.log" | sed 's/^/       /'
    FAILURES=$((FAILURES + 1))
    return
  fi
  html="$(run "curl -s http://127.0.0.1:5000/verify")"
  # Flattened to one line: the two <script> tags are newline-separated by
  # design (see TagBuilder.BuildScriptsHtml), and a default `grep -E` never
  # matches `.` across a newline — the same reason Joomla's own check()
  # flattens before matching.
  html_flat="$(printf '%s' "$html" | tr '\n' ' ')"
  if printf '%s' "$html_flat" | grep -qE "$3"; then
    echo "    ✅ $1"
  else
    echo "    ❌ $1"
    echo "       expected to match: $3"
    echo "       got: $html_flat"
    FAILURES=$((FAILURES + 1))
  fi
  if [ -n "${4:-}" ] && printf '%s' "$html_flat" | grep -qE "$4"; then
    echo "    ❌ $1 — unexpectedly present: $4"
    FAILURES=$((FAILURES + 1))
  fi
}

echo "==> negative control: the page exists, the integration does not"
check "no ciphera/pulse bytes before the package is even referenced" \
  "" \
  "^<!DOCTYPE html>" \
  "ciphera|pulse-analytics|js\\.ciphera\\.net"

# ── Add the integration for real ─────────────────────────────────────────────
echo "==> adding Ciphera.Pulse.Umbraco $VERIFY_VERSION and wiring the TagHelper"
run "cd /work/site && dotnet add package Ciphera.Pulse.Umbraco --version $VERIFY_VERSION --source /work/local-nupkgs"
# The scaffold's own _ViewImports.cshtml has NO trailing newline on its last
# line. `echo … >> file` on a file like that appends directly onto that last
# line, not a new one — this concatenated onto the existing
# "@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers" line and produced
# "…TagHelpers@addTagHelper *, Ciphera.Pulse.Umbraco" as ONE run-on directive.
# Razor still "built" (dotnet build never caught it), but Umbraco's runtime
# view compiler failed every single request with an opaque
# UmbracoCompilationException and no usable inner diagnostic — confirmed by
# rewriting the file with a real newline between the two directives, which
# fixed it outright. printf with a leading \n guarantees the new directive
# starts its own line regardless of whether the file already ends in one.
run "grep -q 'Ciphera.Pulse.Umbraco' /work/site/Views/_ViewImports.cshtml \
      || printf '\n@addTagHelper *, Ciphera.Pulse.Umbraco\n' >> /work/site/Views/_ViewImports.cshtml"
run "cp /work/site/Views/Shared/_Layout.after.cshtml /work/site/Views/Shared/_Layout.cshtml"

CORE='https://js\.ciphera\.net/script\.js'
COMPANION_URL='https://js\.ciphera\.net/script\.interactions\.js'

echo "==> checking the served HTML"

check "domain only" \
  "PulseAnalytics__Domain=example.com" \
  "<script defer data-domain=\"example\\.com\" src=\"$CORE\"></script>" \
  "data-api|script\\.interactions"

check "no domain configured means auto-detect, not a broken attribute" \
  "PulseAnalytics__Domain=" \
  "<script defer src=\"$CORE\"></script>" \
  "data-domain"

check "domain + api + companion, companion carries no domain" \
  "PulseAnalytics__Domain=chosen.dev PulseAnalytics__Companion=true PulseAnalytics__ApiOrigin=https://proxy.example.com/" \
  "data-domain=\"chosen\\.dev\" data-api=\"https://proxy\\.example\\.com\" src=\"$CORE\"></script>.*$COMPANION_URL" \
  ""

# NOT a check() here, by design, confirmed against a real run — see the
# house rule "prove a failure is yours before changing your code":
# `PulseAnalyticsOptionsValidator` runs the IDENTICAL `TagBuilder.IsValidDomain`
# check as `BuildScriptsHtml` itself, wired in with `.ValidateOnStart()`
# (README "A mistyped domain fails loudly, at startup"). For ANY config-bound
# real site, an invalid domain NEVER reaches a running request at all — the
# host throws `OptionsValidationException` and exits before Kestrel binds a
# port, confirmed verbatim in a real run's log:
#   Microsoft.Extensions.Options.OptionsValidationException: PulseAnalytics:Domain
#   'bad_domain.invalid' is not a valid domain. …
# So "an invalid domain is dropped, never injected" is not a reachable
# real-install scenario — it describes `TagBuilder.BuildScriptsHtml`'s own
# defensive behaviour for a caller that never goes through Options validation
# at all, and that is exactly what
# `TagBuilderTests.Invalid_domain_is_dropped_never_injected` already proves,
# with no host needed. The real-install equivalent that actually matters —
# a bad domain never serving a live page — is the next section below
# ("an invalid domain refuses to start the site, loudly"), which is a
# DIFFERENT, deliberately stricter claim and the one a real Umbraco site
# actually makes.

echo "==> checking a surface-controller page under Umbraco's own backoffice path prefix"
html_backoffice="$(run "curl -s http://127.0.0.1:5000/umbraco/surface/VerifySurface/Index")"
if printf '%s' "$html_backoffice" | grep -qE "ciphera|js\\.ciphera\\.net"; then
  echo "    ❌ a page under /umbraco/surface/... carries the tag — the backoffice guard did not hold"
  FAILURES=$((FAILURES + 1))
else
  echo "    ✅ a page under /umbraco/surface/... carries no tag"
fi
echo "    (the same shared layout DOES carry the tag at /verify, proven by the checks above)"

echo "==> checking that an invalid domain refuses to start the site, loudly"
kill_site
run "cd /work/site && ASPNETCORE_ENVIRONMENT=Development PulseAnalytics__Domain='not a domain' timeout 20 dotnet run --no-launch-profile --urls http://127.0.0.1:5000 > /work/site-badconfig.log 2>&1; echo \"exit:\$?\"" > /tmp/pulse-verify-badconfig.out
EXIT_LINE="$(tail -n1 /tmp/pulse-verify-badconfig.out)"
if [ "$EXIT_LINE" = "exit:0" ]; then
  echo "    ❌ the site started successfully with an invalid PulseAnalytics:Domain — ValidateOnStart did not fire"
  FAILURES=$((FAILURES + 1))
else
  if run "grep -q 'PulseAnalytics:Domain' /work/site-badconfig.log"; then
    echo "    ✅ startup refused an invalid domain, and said so"
  else
    echo "    ❌ startup failed, but not with the expected PulseAnalytics:Domain message — see /work/site-badconfig.log"
    FAILURES=$((FAILURES + 1))
  fi
fi

if [ "$MUTATE" -eq 1 ]; then
  echo "==> --mutate: breaking the backoffice guard and proving the check above goes red"
  # /repo is mounted read-only on purpose — this package's own source tree
  # is never the thing that gets mutated. Copy the WHOLE repo (not just
  # src/), so Directory.Build.props and the README the csproj packs both
  # still resolve by their existing relative paths.
  run "rm -rf /work/mutated-repo && cp -r /repo /work/mutated-repo"
  run "sed -i '/MUTATE:BACKOFFICE_GUARD:BEGIN/,/MUTATE:BACKOFFICE_GUARD:END/d' \
        /work/mutated-repo/src/Ciphera.Pulse.Umbraco/PulseAnalyticsTagHelper.cs"

  MUTATED_FILE="/work/mutated-repo/src/Ciphera.Pulse.Umbraco/PulseAnalyticsTagHelper.cs"
  if run "grep -q 'MUTATE:BACKOFFICE_GUARD' $MUTATED_FILE"; then
    echo "    ❌ the mutation markers are still present — sed did not remove the guard; aborting --mutate"
    FAILURES=$((FAILURES + 1))
  else
    MUTATED_VERSION="${VERIFY_VERSION}-mutated"
    run "dotnet pack /work/mutated-repo/src/Ciphera.Pulse.Umbraco/Ciphera.Pulse.Umbraco.csproj \
          -c Release -o /work/local-nupkgs -p:Version=$MUTATED_VERSION"
    run "cd /work/site && dotnet add package Ciphera.Pulse.Umbraco --version $MUTATED_VERSION --source /work/local-nupkgs"

    if ! start_site "PulseAnalytics__Domain=example.com"; then
      echo "    ❌ mutated build would not even start — cannot prove the mutation test"
      FAILURES=$((FAILURES + 1))
    else
      html_mutated="$(run "curl -s http://127.0.0.1:5000/umbraco/surface/VerifySurface/Index")"
      if printf '%s' "$html_mutated" | grep -qE "ciphera|js\\.ciphera\\.net"; then
        echo "    ✅ with the guard removed, the backoffice check now correctly goes RED — the assertion is real"
      else
        echo "    ❌ the guard was removed and the page STILL carries no tag — the backoffice check is not testing what it claims to"
        FAILURES=$((FAILURES + 1))
      fi
    fi
  fi
fi

echo
if [ "$FAILURES" -eq 0 ]; then
  echo "Umbraco: ALL CHECKS PASSED"
else
  echo "Umbraco: $FAILURES FAILURE(S)"
fi
exit "$FAILURES"
