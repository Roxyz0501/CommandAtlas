# Verification — 1.1.0.0 — 2026-10-14 JST

- Clean Release succeeded with zero warnings/errors. ZIP SHA-256: `7D2E4012EAA079CB886C7CD3AF42E1F45A23FC4D401BB0BA5890A592A523B1A6`.
- 353 catalog/localization, 7 icon-cache, 19 new feature/cache checks and 4,820 isolated ImGui/glyph/lifecycle/access checks passed.
- The actual rendered Run/Copy buttons were clicked: copy executes nothing, a valid subcommand executes exactly once, and placeholder arguments disable execution. Disabled plugins are excluded. Browsing causes zero extra registry reads/config writes; explicit execution adds one current command-registry lookup to revalidate ownership.
- GTranslate 2.4.0 GoogleTranslator2 live test translated two generic English descriptions into Japanese in one API-key-free batch (20 feature checks including live). The older GoogleTranslator endpoint returned 429 during research; production uses GoogleTranslator2 only. Availability is not guaranteed by this test.
- Translation mock measurements: 100 duplicate lookups and two distinct descriptions yield one batch request; repeated view and a new cache instance yield zero translation requests; off/hidden/cleared queue yields zero new requests; 100 retries during failure backoff yield one failed request total. No business DB exists.
- Translation limits: visible descriptions only; up to 8 descriptions / 3,500 source characters per batch, one worker, minimum 2 seconds between successful batches; 5-minute failure backoff. Individual source 2,000 characters, queue 64; response buffer 1 MiB. Markers are validated before applying any batch. Network is cancelled when inactive.
- Translation cache: schema/provider-specific hash of source, translated text and expiry; no source text persisted. TTL 90 days, 512 entries, 400,000 translated UTF-16 characters, load cap 4 MiB. One atomic cache replacement per successful batch; browsing warm data writes nothing. Changed source invalidates by hash. Storage/network failures fall back to memory/original text. No credentials/cookies sent.
- GTranslate DLL and complete MIT license are included. Manual descriptions, plugin names and executable command text are not sent for translation. UI translation defaults off and survives config migration.
- Subcommands come only from explicit matching-root help examples or user presets (maximum 256). No complete arbitrary-plugin subcommand discovery is claimed.
- Japanese/German production-render snapshots visually inspected in `artifacts/ui-v1.1`. Tests use stubs and isolated ImGui; actual game integration, clipboard/IME/scaling and host font-atlas lifecycle remain unverified.

## Historical verification for 1.0.0.0

# Verification — 2026-10-14 JST

## Build and package

- Clean Release: .NET 10.0.401, Dalamud.NET.Sdk 15.0.0, locally installed API 15 references. Zero warnings/errors.
- Package: `artifacts/CommandAtlas-1.0.0.0.zip`.
- SHA-256: `85BA8AA6560BB7146A92EA5825083046721CEA2B3ABA5134169C68A372E919AC`.
- ZIP contains DLL, dependency manifest, plugin manifest, MIT LICENSE, NOTICE and original 512×512 PNG icon. Seven-language resources are embedded in the DLL.
- Packaged author/version/RepoUrl/IconUrl checked; common tab source hash exactly matches workspace template.
- The user authorized publication on 2026-10-14. This document records pre-release verification; final public URL and shared-index verification is recorded in `PUBLICATION_STATE.md`.
- Source checked for common token/private-key signatures and developer-specific absolute paths; no matches. Compiled DLL checked for drive-letter absolute paths; no matches. Build/test outputs are excluded by `.gitignore`; the ZIP uses an explicit allowlist.

## Automated checks

- 213 catalog/localization checks: plugin/description/command search, hidden-command setting, stopped plugins, empty results, language normalization, saved choice precedence, game/UI/final fallback and all seven resource columns.
- 7 cache assertions with a simulated HTTP handler: concurrent request coalescing, warm disk cache, ETag/304 handling, failure backoff, invalidation and HTTPS requirement.
- 3,573 isolated ImGui/glyph/lifecycle/access checks: actual production drawing, seven languages, Noto CJK glyph coverage, expansion, one-click clipboard callback, no command execution, no reads/writes while viewing/searching, and own-handler cleanup.
- The clipboard test replaces the native clipboard callback in the isolated process; it does not modify the user's OS clipboard. The example command handler throws if executed, and was not executed.
- Production rendering screenshots in `artifacts/ui/`. Japanese and German screenshots visually inspected. These use test plugin metadata and texture stubs, not the game or installed user plugins.
- The UI test project emitted NU1900 because NuGet vulnerability data was unreachable. Tests completed successfully. This is not a successful dependency vulnerability audit; the production clean Release build itself had no warnings.

## Data access and limits

The following are measured in the isolated fixture, not production game telemetry. There is no business DB, server API, chat dispatch, packet send or game configuration write.

| Scenario | Catalog reads | Config writes | HTTP / response body |
| --- | --- | --- | --- |
| First opened catalog, fixture of 3 plugins / 2 commands | InstalledPlugins once + Commands once | 0 with saved language | 0 for fixture icons |
| Repeated frames in seven languages | 0 additional | 0 | 0 |
| Search and tab navigation | 0 additional | 0 | 0 |
| 100 same-URL icon lookups before completion | n/a | 0 | 1 request / 24 simulated bytes |
| Same icon in a new cache instance within TTL | n/a | 0 | 0 requests / 0 bytes |
| Expired icon with ETag and unchanged response | n/a | 0 | 1 request / 0 body bytes |
| Missing icon and 100 further lookups in backoff | n/a | 0 | 1 failed request total |

Design targets: no periodic catalog polling; one in-memory snapshot per dirty refresh; no disk writes from browsing/search/tab changes. Plugin-change events invalidate the snapshot; refreshing resolves registered handler assemblies through public `GetPlugin(Assembly)`. Assemblies are resolved once per refresh and are not retained afterwards, allowing plugin unload. A manual refresh reads the snapshot sources again to pick up dynamic commands. Native event timing remains unverified.

Icons are requested only for visible cards, at most one request in flight, with duplicate requests merged. Maximum 128 URL entries and 128 persistent PNG files (64 MiB image-body upper bound plus small metadata); each response is limited to 512 KiB and dimensions to 1024×1024. Cache schema uses an `icon-cache-v1` directory. TTL is seven days, with conditional ETag requests when available. Missing ETags require a bounded image-body request after expiration. Failures back off for ten minutes; changed URLs use separate keys. Entries beyond capacity show a placeholder. This is an explicit capacity tradeoff, not an unlimited cache. PNG bodies and metadata are local cache writes, not business DB writes.

No secret/authenticated data is cached; requests send no cookies or credentials and redirects are disabled. Failed refreshes use a placeholder. Cache/storage errors also fall back to a placeholder. Network work is cancelled on plugin disposal. Successfully changed images invalidate the host texture cache. If the UI is closed there are no new icon requests, although an already-started request can finish.

## Remaining live acceptance

- Load the ZIP in Dalamud and verify actual installed plugin grouping, official and third-party icon endpoints, dynamic plugin load/unload, real clipboard paste, UI scaling/IME and host-managed font atlas changes.
- Commands implemented through shared-library handlers may not have a resolvable owner and are displayed in the unassigned/Dalamud group.
- Stopped plugins cannot provide runtime command registrations; no untrusted DLL is loaded to discover them.
- External names/help/descriptions retain their original language. No inferred subcommands or arguments are claimed.
- Public release would additionally require repository/icon/ZIP HTTP verification and registration in the existing shared repository.

## API references

- https://dalamud.dev/api/Dalamud.Plugin/Interfaces/IDalamudPluginInterface/
- https://dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/ICommandManager/
- https://dalamud.dev/api/Dalamud.Plugin/Interfaces/IExposedPlugin/
- DIP17 icon-address behavior: goatcorp/Dalamud `PluginImageCache.cs`, source blob `dca38e3e090559a9d1c4fb60ac6a810d72d8e26c`, inspected through the GitHub connector. Only the address convention was referenced, not its implementation.

## 1.1.0.0 dependency audit

NuGet vulnerability query including transitive packages succeeded on 2026-10-14 and reported no known vulnerable packages. This supersedes the historical 1.0.0.0 feed limitation above.
