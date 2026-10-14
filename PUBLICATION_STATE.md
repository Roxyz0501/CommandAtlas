# Command Atlas publication state

## 2026-10-14 — 1.0.0.0 public preview

- Explicitly published at the user's request; author Roxyz0501.
- Repository: https://github.com/Roxyz0501/CommandAtlas
- Release: https://github.com/Roxyz0501/CommandAtlas/releases/tag/v1.0.0.0
- ZIP: https://github.com/Roxyz0501/CommandAtlas/releases/download/v1.0.0.0/CommandAtlas-1.0.0.0.zip
- Source/tag commit: `404b4dd0daa575f7b9b5fa1fe46d0d91244caa0f`.
- ZIP SHA-256: `85BA8AA6560BB7146A92EA5825083046721CEA2B3ABA5134169C68A372E919AC`.
- Anonymous repository, PNG icon and Release ZIP returned HTTP 200. Downloaded ZIP hash, six package entries, author, version, RepoUrl and IconUrl verified.
- Shared registration commit: `12ed63f569529ab2b6f5ffa964c7434fc410a73f`. Eight entries verified through the connector and anonymous commit-pinned index; the seven existing entries are unchanged. Normal main URL still returned the previous seven-entry response at verification (max-age 300); installer visibility can be delayed by CDN propagation.
- Clean Release zero warnings/errors; 213 catalog/localization checks, 7 cache checks and 3,573 isolated ImGui/glyph/copy/lifecycle/access checks passed. UI-test NuGet vulnerability feed remained unreachable (NU1900); not represented as a successful vulnerability audit.
- Native game operation and actual installed-plugin icon/grouping integration remain unverified and are disclosed in README/Release notes. No installed plugin DLL was replaced.
- GitHub connector used for source/icon/index writes and verification. CLI used only for unsupported repository creation/Release asset publishing and local Git synchronization.

