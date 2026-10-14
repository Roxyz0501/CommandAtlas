# Command Atlas publication state

## 2026-10-14 — Command Atlas 1.1.0.0 public preview

- Release: https://github.com/Roxyz0501/CommandAtlas/releases/tag/v1.1.0.0
- ZIP: https://github.com/Roxyz0501/CommandAtlas/releases/download/v1.1.0.0/CommandAtlas-1.1.0.0.zip
- Source/tag commit: `fc79ea7c0899425db8fa21a5b15e5a5d2dba32fb`.
- ZIP SHA-256: `7D2E4012EAA079CB886C7CD3AF42E1F45A23FC4D401BB0BA5890A592A523B1A6`.
- Added per-command execution/copy, editable arguments, help-derived and saved manual subcommands, disabled-plugin exclusion, and optional cached Japanese descriptions through GTranslate 2.4.0 / GoogleTranslator2. User explicitly approved sending descriptions only with translation enabled. Default is off.
- Clean Release: zero warnings/errors; 353 catalog/localization, 7 icon-cache, 19 offline feature/cache and 4,820 isolated ImGui/glyph/lifecycle/access checks passed. Live Google batch translation succeeded (20 feature checks including live); NuGet query reported no known vulnerable dependencies.
- Anonymous repository, PNG icon and ZIP returned HTTP 200. Downloaded ZIP matched the verified hash; eight allowlisted entries include GTranslate DLL and full MIT license. Packaged RepoUrl/IconUrl/version checked; canonical tabs unchanged.
- Shared index commit: `8d5111005795e587f8aee7433dcfc6d071d0ea23`. Eight entries verified at anonymous commit-pinned URL; seven other entries unchanged. Normal main URL initially served cached 1.0.0.0.
- Native game integration remains unverified and disclosed as a public preview. No installed plugin DLL was replaced. Source/index writes used the GitHub connector; CLI was used for unsupported Release publishing and Git synchronization.

## 2026-10-14 — 1.0.0.0 public preview

- Explicitly published at the user's request; author Roxyz0501.
- Repository: https://github.com/Roxyz0501/CommandAtlas
- Release: https://github.com/Roxyz0501/CommandAtlas/releases/tag/v1.0.0.0
- ZIP: https://github.com/Roxyz0501/CommandAtlas/releases/download/v1.0.0.0/CommandAtlas-1.0.0.0.zip
- Source/tag commit: `404b4dd0daa575f7b9b5fa1fe46d0d91244caa0f`.
- ZIP SHA-256: `85BA8AA6560BB7146A92EA5825083046721CEA2B3ABA5134169C68A372E919AC`.
- Anonymous repository, PNG icon and Release ZIP returned HTTP 200. Downloaded ZIP hash, six package entries, author, version, RepoUrl and IconUrl verified.
- Shared registration commit: `12ed63f569529ab2b6f5ffa964c7434fc410a73f`. Eight entries verified through the connector and anonymous commit-pinned index; the seven existing entries are unchanged. The anonymous normal main URL also returned HTTP 200 with all eight entries and Command Atlas 1.0.0.0 after the initial stale CDN response refreshed.
- Clean Release zero warnings/errors; 213 catalog/localization checks, 7 cache checks and 3,573 isolated ImGui/glyph/copy/lifecycle/access checks passed. UI-test NuGet vulnerability feed remained unreachable (NU1900); not represented as a successful vulnerability audit.
- Native game operation and actual installed-plugin icon/grouping integration remain unverified and are disclosed in README/Release notes. No installed plugin DLL was replaced.
- GitHub connector used for source/icon/index writes and verification. CLI used only for unsupported repository creation/Release asset publishing and local Git synchronization.

