# Caption Studio の開発ガイド

## 実装方針

- アプリは `CaptionStudio/`、字幕更新の単体テストは `CaptionStudio.Tests/` に置く。.NET 10 SDK は `mise.toml` で管理する。
- C# と標準の WinUI 3 コントロールを使い、変更に必要なファイルと依存関係に絞る。OCR、独自音声認識、外部サーバーは使わない。
- ライブ キャプションの字幕は Windows UI Automation で取得する。要素の探索と読み取りは UI スレッドから分離し、字幕要素は安定した AutomationId で探す。 [Microsoft の UI Automation スレッド指針](https://learn.microsoft.com/ja-jp/windows/win32/winauto/uiauto-threading)
- 字幕の更新処理では認識途中の訂正と表示範囲の移動を扱う。同じ発言の繰り返しを残すため、過去の全文を使った一律の重複排除は避ける。

## スキル

- WinUI の変更では `winui-dev-workflow` と `winui-design` を参照する。セットアップやテンプレートの確認には `winapp-setup` を使う。
- 実機での UI 検証には `winui-ui-testing` と `winapp-ui-automation` を使う。余白や状態表示などを見直すときは、必要に応じて `impeccable` の `polish` を使う。
- README など日本語の文書を推敲するときは `yomiyasu` を使う。

スキルを再導入する場合は、Node.js が入った環境でリポジトリのルートから次を実行する。

```powershell
npx --yes skills add 'https://github.com/microsoft/win-dev-skills.git#v0.7.1' -a codex --skill winui-dev-workflow --skill winui-design --skill winui-ui-testing -y
npx --yes skills add 'https://github.com/microsoft/winappCli.git#v0.7.1' -a codex --skill winapp-ui-automation --skill winapp-setup -y
npx --yes skills add 'https://github.com/pbakaus/impeccable.git#engine-v0.1.19' -a codex --skill impeccable -y
npx --yes skills add 'https://github.com/nanaism/yomiyasu.git#v2.5.0' -a codex --skill yomiyasu -y
```

## 変更の検証

Windows でリポジトリのルートに移動し、`mise install` に続けて次のコマンドを実行する。GitHub Actions でも同じコマンドで整形、ビルド、Roslyn の静的解析、単体テストを確認する。

```powershell
mise exec -- dotnet restore CaptionStudio.slnx
mise exec -- dotnet format CaptionStudio.slnx --verify-no-changes --no-restore
mise exec -- dotnet build CaptionStudio/CaptionStudio.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=false -warnaserror --no-restore
mise exec -- dotnet test CaptionStudio.Tests/CaptionStudio.Tests.csproj -c Release -p:TreatWarningsAsErrors=true --no-restore
```

- 字幕の差分処理を変えたら、訂正、表示範囲の移動、同じ字幕を続けて読み取った場合など、変更した挙動を単体テストで確認する。
- UI を変えたら `winapp run` で起動し、`winapp ui` で取得の開始・停止、状態表示、保存した TXT の内容を確認する。日本語の字幕を検証するときはライブ キャプションと Caption Studio を同じ Windows セッションで起動する。日本語の音声を再生し、字幕を取得できることを確認する。
- 実施できなかった検証は理由とともに報告する。変更は意味のまとまりごとにコミットする。
