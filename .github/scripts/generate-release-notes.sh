#!/usr/bin/env bash
# Builds the "## What's Changed" body for a GitHub release.
#
# A plain `git log` between releases only shows one line per squash-merged PR
# (GitHub squash commits are titled "<PR title> (#123)"), which hides the
# individual commits that made up the PR. This expands each of those via the
# GitHub API (pulls/{n}/commits) so the release notes show the underlying
# changes, not just the PR title. Commits made directly on master, and PRs
# merged with a real merge commit (git log --no-merges already shows their
# individual commits), are listed as-is.
set -euo pipefail

repo="$1"      # owner/repo
from_tag="$2"  # previous release tag, or "" if there isn't one yet
to_sha="$3"    # commit the release is being cut from
image="$4"     # e.g. ghcr.io/owner/qa-tracker
version="$5"   # e.g. 2026.09.22

range="$to_sha"
[ -n "$from_tag" ] && range="${from_tag}..${to_sha}"

echo "## What's Changed"
echo

while IFS=$'\t' read -r sha subject; do
  if [[ "$subject" =~ ^(.+)\ \(#([0-9]+)\)$ ]]; then
    pr="${BASH_REMATCH[2]}"
    mapfile -t commits < <(gh api "repos/$repo/pulls/$pr/commits" --jq '.[].commit.message | split("\n")[0]' 2>/dev/null || true)
    if [ "${#commits[@]}" -eq 0 ]; then
      echo "- $subject"
    else
      for c in "${commits[@]}"; do
        [[ "$c" =~ ^Merge\  ]] && continue
        echo "- $c"
      done
    fi
  else
    echo "- $subject"
  fi
done < <(git log --no-merges --pretty=format:'%H%x09%s' "$range") | awk '!seen[$0]++'

echo
echo "Pull this version: \`docker pull ${image}:${version}\`"
