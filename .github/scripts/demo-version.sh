#!/usr/bin/env bash
set -euo pipefail

if [[ "${GITHUB_REF_TYPE:-}" == tag ]]; then
  tag="${GITHUB_REF_NAME}"
  if [[ ! "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+-demo\.[0-9]+$ ]]; then
    echo "::error::Use a demo tag like v0.1.0-demo.2" >&2
    exit 1
  fi
  printf '%s\n' "$tag"
else
  version="$(awk '$1 == "bundleVersion:" { print $2 }' ProjectSettings/ProjectSettings.asset)"
  if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "::error::Expected a numeric Unity bundleVersion such as 0.1.0" >&2
    exit 1
  fi
  printf 'v%s-%s\n' "$version" "${GITHUB_SHA:0:7}"
fi
