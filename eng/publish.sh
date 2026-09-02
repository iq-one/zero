#!/usr/bin/env bash
#
# Publishes the packages to nuget.org from a developer machine.
#
# THE NORMAL PATH IS CI, NOT THIS. Tag a commit and the publish job pushes with Trusted
# Publishing: the workflow proves who it is with its OIDC identity and nuget.org hands back
# a key that lives for minutes. Nothing is stored and nothing can leak.
#
#   git tag v0.1.0 && git push origin v0.1.0
#
# This script exists for the case CI cannot cover -- a first release before the trusted
# publishing policy is approved, or a push from somewhere GitHub cannot reach. It needs a
# long-lived API key, which is the thing Trusted Publishing exists to avoid, so prefer the
# tag whenever the tag will do.
#
# Publishing is not reversible: nuget.org does not delete a version, only unlists it, and a
# version number is never reusable afterwards. So this script refuses to guess. It checks
# everything it can check first, prints what it is about to push, and pushes only when told.
#
#   NUGET_API_KEY=... ./eng/publish.sh --dry-run     what would be pushed, and why it is ready
#   NUGET_API_KEY=... ./eng/publish.sh               push
#
# The key comes from the environment and is never written to a file, echoed, or passed
# anywhere but the push itself.

set -euo pipefail

cd "$(dirname "$0")/.."

dry_run=false
[[ "${1:-}" == "--dry-run" ]] && dry_run=true

source="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
packages="artifacts/packages"

fail() { echo "publish: $1" >&2; exit 1; }

# ---- checks -------------------------------------------------------------------------

$dry_run || [[ -n "${NUGET_API_KEY:-}" ]] \
  || fail "NUGET_API_KEY is not set. Get a key from nuget.org and pass it in the environment."

git diff --quiet && git diff --cached --quiet \
  || fail "the working tree has uncommitted changes. What is published has to be what is committed."

git remote get-url origin > /dev/null 2>&1 \
  || fail "no git remote named 'origin'. SourceLink stamps the repository and commit into every
         package; without a remote, the packages ship with a source link that resolves nowhere."

echo "--- the suite ---"
dotnet test IQOne.Zero.slnx -c Release --nologo -v q

echo "--- packing ---"
rm -f "$packages"/*.nupkg "$packages"/*.snupkg
dotnet pack IQOne.Zero.slnx -c Release --nologo -v q

echo "--- against a real consumer ---"
./eng/verify-consumer.sh
ZERO_VERIFY_TFM=net8.0 ./eng/verify-consumer.sh

# ---- what would be pushed -----------------------------------------------------------

mapfile -t nupkgs < <(ls "$packages"/*.nupkg 2>/dev/null | sort)

[[ ${#nupkgs[@]} -gt 0 ]] || fail "no packages in $packages."

version="$(basename "${nupkgs[0]}" .nupkg | sed 's/^.*\.\([0-9].*\)$/\1/')"

echo
echo "About to push ${#nupkgs[@]} packages at $version to $source:"
printf '  %s\n' "${nupkgs[@]##*/}"
echo

# A version already on the source is a version that cannot be replaced. Saying so here is
# cheaper than reading it out of a partially-failed push.
if command -v curl > /dev/null; then
  first="$(basename "${nupkgs[0]}" ".$version.nupkg" | tr '[:upper:]' '[:lower:]')"
  taken="$(curl -fsS "https://api.nuget.org/v3-flatcontainer/$first/index.json" 2>/dev/null \
             | tr -d ' "' | grep -c "^$version,\?$" || true)"

  [[ "$taken" == "0" ]] \
    || fail "$version is already published. nuget.org never lets a version be replaced —
           raise VersionPrefix in build/Packaging.props and start again."
fi

if $dry_run; then
  echo "Dry run: nothing was pushed."
  exit 0
fi

# ---- push ---------------------------------------------------------------------------
#
# --skip-duplicate so a retry after a partial failure finishes the job instead of stopping
# on the packages that already landed.

for nupkg in "${nupkgs[@]}"; do
  echo "pushing ${nupkg##*/}"

  dotnet nuget push "$nupkg" \
    --source "$source" \
    --api-key "$NUGET_API_KEY" \
    --skip-duplicate
done

echo
echo "Pushed $version. Symbols went with them (.snupkg)."
echo "Tag the commit so the published version can be found again:"
echo "  git tag v$version && git push origin v$version"
