# Command Atlas

Roxyz0501 が開発する、インストール済み Dalamud プラグインのコマンド一覧です。

- プラグインを名前・アイコン・説明付きで一覧表示。
- 各プラグインをクリックして、登録済みコマンドとヘルプを展開。
- コマンドを見やすい表で表示し、行ごとに「実行」と「コピー」のボタンを用意。引数を編集して使用できます。
- 名前・プラグイン説明・コマンド・コマンド説明を横断検索。検索時は該当項目を展開。
- 無効化・停止中のプラグインは非表示。所属を確定できないコマンドは「所属を特定できないコマンド / Dalamud」に表示。
- 20プラグインずつのページ表示。ヘルプに非表示指定されたコマンドは設定で表示可能。

## 使い方

`/commandatlas` で開き、プラグイン名の左の三角を押して展開します。必要な引数を行の入力欄で編集し、「実行」または「コピー」を押してください。`<name>` 等の未入力プレースホルダーがある間は実行できません。

プラグインの読込・停止等の変更時に一覧を更新します。プラグインが動作中にコマンドを追加・変更する場合は「再取得」を押してください。

ヘルプに明記されたサブコマンド例を抽出します。検出できないものは「サブコマンドを追加」から説明付きで保存できます（最大256件）。各プラグインの登録済みルートコマンドだけが対象です。説明未登録や共有ライブラリ経由で所属不明の場合には制限があります。すべてのサブコマンドを自動取得できるわけではありません。FFXIV標準コマンド全体を収録するものではありません。

公式プラグインのアイコンは Dalamud の DIP17 配布チャネルから、外部プラグインは `IconUrl` から取得します。HTTPS PNG、512 KiB以下、最大1024×1024に対応します。取得できない画像、URLがない画像、上限超過時は代替表示になります。開発用プラグインの非公開ローカルパスを探索しません。自身のアイコンは同梱ファイルを使います。

## インストール

Dalamudの設定から、カスタムプラグインリポジトリに次のURLを追加してください。

```text
https://raw.githubusercontent.com/Roxyz0501/DalamudPluginRepo/main/repo.json
```

プラグインインストーラーで **Command Atlas** を検索してインストールします。既にこの共通リポジトリを登録している場合は追加不要です。`/commandatlas` で開きます。

[バージョン1.1.0.0のRelease](https://github.com/Roxyz0501/CommandAtlas/releases/tag/v1.1.0.0) からZIPも取得できます。ゲーム内の実機動作は未確認の公開プレビューです。

### 開発用に読み込む

`artifacts/CommandAtlas-1.1.0.0.zip` を専用フォルダーに展開し、Dalamud開発者設定の開発用プラグインの場所へ `CommandAtlas.dll` を追加して読み込みます。ゲーム内操作・実環境の全プラグインとの互換性は未確認です。

## 言語

日本語、English、Deutsch、Français、한국어、简体中文、繁體中文に対応。「言語設定」タブで変更できます。
初回のみゲーム言語、次に Dalamud UI言語、最後にEnglishの順で決定して保存します。以後は保存した選択を維持します。ランチャー言語を直接取得する公開APIは使用せず、非公開設定も読み取りません。
プラグイン名・コマンド文字列は原文を保持します。「説明を和訳（Google）」をオンにすると、表示中のプラグイン説明とコマンドヘルプをGTranslate経由でGoogleへ送信して日本語に翻訳します。APIキー不要、初期状態はオフです。手動で追加した説明は送信しません。結果は端末内に最大90日・512件・合計40万文字まで保存し、再利用します。原文は翻訳結果のツールチップで確認できます。失敗・未対応時は原文を表示します。画面を閉じるか和訳をオフにすると新しい翻訳通信を停止します。

## ビルドと検証

.NET SDK 10、Dalamud API 15 / Dalamud.NET.Sdk 15.0.0が必要です。

```powershell
dotnet build CommandAtlas/CommandAtlas.csproj -c Release
dotnet run --project Tests/Tests.csproj -c Release
dotnet run --project Tests/UI/UiTests.csproj -c Release -- artifacts/ui
./package.ps1
```

検証結果・通信量・キャッシュ制限は [VERIFICATION.md](VERIFICATION.md) を参照してください。

## 参照・ライセンス

- 実装本体: Roxyz0501、MIT License。
- [GTranslate 2.4.0](https://github.com/d4n3436/GTranslate): d4n3436、MIT License。GoogleTranslator2を使用し、DLLとライセンス全文を同梱します。キー不要のGoogle翻訳接続はサービス側の変更や制限により利用できなくなる場合があります。
- [Dalamud](https://github.com/goatcorp/Dalamud): goatcorp、LGPL-3.0。公開APIの `InstalledPlugins`、`GetPlugin(Assembly)`、`ICommandManager.Commands`、テクスチャ・フォントAPIを利用します。Dalamud本体を同梱しません。
- 公式アイコンのDIP17 URL規則は [Dalamud PluginImageCache](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Interface/Internal/Windows/PluginImageCache.cs) を参照しました。実装コードの複製は行っていません。
- `StandardPluginTabs.cs` はワークスペースの共通実装をそのまま使用。フォント補助は Roxyz0501 の BaitBeacon、隔離描画テストの補助は RetainerRecall のコードを再利用しています。
- 各プラグインの名前、説明、ヘルプ、アイコンは各作者のコンテンツです。実行時に表示し、配布物には同梱しません。
- Noto Sans CJK は Dalamud が提供する SIL Open Font License 1.1 のフォントを利用します。

## 任意の支援

開発者 Roxyz0501 への支援: [Ko-fi](https://ko-fi.com/roxyz0501)。支援の有無による機能制限はありません。
