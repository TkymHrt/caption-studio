# Caption Studio の開発ガイド

## 実装方針

- Windows 11 のライブ キャプションを読み取る個人用アプリ。C#、.NET 10、WinUI 3、Windows UI Automation を使う。
- UI と取得処理は `CaptionStudio/` に置く。字幕更新のテストは `CaptionStudio.Tests/` に置く。変更に必要なファイルと依存関係に絞る。
- 字幕の更新処理では、認識途中の訂正と表示範囲の移動を扱う。同じ発言が実際に繰り返されるため、過去の全文を使った一律の重複排除は避ける。
- UI Automation の探索と読み取りは UI スレッドから分離する。字幕要素は安定した AutomationId を使って探す。 [Microsoft のスレッド指針](https://learn.microsoft.com/ja-jp/windows/win32/winauto/uiauto-threading)
- 画面は標準の WinUI コントロールで構成する。OCR、独自音声認識、外部サーバーを導入しない。

## Microsoft 公式スキル

WinUI の変更時は `winui-dev-workflow` と `winui-design`、実機検証時は `winui-ui-testing` と `winapp-ui-automation` を参照する。セットアップやテンプレートの確認には `winapp-setup` を使う。スキルを再導入する場合は、Node.js の `npx` でリポジトリのルートから実行する。

```powershell
npx --yes skills add 'https://github.com/microsoft/win-dev-skills.git#v0.7.1' -a codex --skill winui-dev-workflow --skill winui-design --skill winui-ui-testing -y
npx --yes skills add 'https://github.com/microsoft/winappCli.git#v0.7.1' -a codex --skill winapp-ui-automation --skill winapp-setup -y
```

## 変更の検証

- `mise install` で `mise.toml` の .NET SDK を用意する。コマンドと実機検証の手順は [README.md](README.md#開発と検証) に従う。
- `dotnet format` と Roslyn の静的解析を通す。字幕の差分処理を変えたら、訂正、スクロール、同一スナップショットなど、変更した挙動を単体テストで確認する。
- UI を変えたら `winapp run` で起動し、`winapp ui` で開始・停止、状態表示、保存を確認する。日本語字幕の取得はライブ キャプションとアプリを同じ Windows セッションで起動して検証する。
- 実施できなかった検証は理由とともに報告する。変更は意味のまとまりごとにコミットする。
