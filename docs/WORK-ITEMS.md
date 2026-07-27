# Work Items Log

Lightweight internal work-item tracking (no external issue tracker — see `adr/0003-repository-branching-delivery-strategy.md`). Add one row per work item before starting the corresponding branch, so every `feature/{CODE}-{seq}-...` branch and PR has a traceable entry here.

| Key | Context | Title | Status | Linked PRD Chapter / Day | Notes |
|---|---|---|---|---|---|
| PLAT-001 | Platform (CI) | Bump GitHub Actions (checkout/setup-dotnet/cache/upload-artifact) to Node 24-native major versions | Open | Day 11 (CI Pipeline Setup) | GitHub Actions run #1 (commit 15d554e) succeeded but warned that actions/checkout@v4, actions/setup-dotnet@v4, actions/cache@v4, actions/upload-artifact@v4 target the now-deprecated Node 20 runtime; GitHub is auto-forcing them onto Node 24 as a temporary shim. Not urgent (nothing broken), but should be bumped to the next major version once available, before the shim is retired. |
