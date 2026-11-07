# Caption Studio

Windows 11 のライブ キャプションをリアルタイムで表示し、UTF-8 のテキストファイルに保存する個人用アプリです。C#、.NET 10、WinUI 3 で構成し、Windows UI Automation で字幕を読み取ります。

## 起動

Windows 11 x64、mise、WinApp CLI 0.7.1 以降が必要です。開発用パッケージの登録には Windows の開発者モードを使います。WinApp CLI は `winget install --id Microsoft.WinAppCli --exact` で導入できます。 [mise](https://mise.jdx.dev/)

リポジトリのルートで次を実行します。

```powershell
mise install
mise exec -- winapp run CaptionStudio/CaptionStudio.csproj --arch x64
```

.NET SDK は `mise.toml` で管理します。起動用の .NET ランタイムはアプリに同梱されます。

## 使い方

1. `Win` + `Ctrl` + `L` で Windows のライブ キャプションを開きます。ライブ キャプションの設定で日本語を選び、初回は必要に応じて言語ファイルをダウンロードします。 [Microsoft の設定手順](https://support.microsoft.com/ja-jp/accessibility/windows/use-live-captions-to-better-understand-audio)
2. Caption Studio で **取得開始** を押します。ライブ キャプションが閉じている間は待機し、開くと自動で接続します。
3. **取得停止** を押すと取得を止め、画面に表示した字幕は保持します。
4. **TXT に保存** を押し、Windows の保存ダイアログで保存先を選びます。ボタンを押した時点の字幕を UTF-8（BOM なし）で書き出します。画面に表示された字幕は、アプリを閉じるまでメモリに保持します。

## 字幕取得の範囲

ライブ キャプションの表示内容を約 250 ミリ秒間隔で読み取り、認識途中の書き換えと表示範囲の移動に追従します。保存の対象は UI Automation を通じて読み取れた字幕です。取得を再開すると、ライブ キャプションに残っている字幕から読み取ります。Windows の更新で字幕要素が変わる場合は取得先の見直しが必要です。

## 開発と検証

GitHub Actions の Windows ジョブでビルド、Roslyn の静的解析、整形チェック、字幕更新の単体テストを実行します。ローカルではリポジトリのルートから次を実行します。

```powershell
mise exec -- dotnet restore CaptionStudio.slnx
mise exec -- dotnet format CaptionStudio.slnx --verify-no-changes --no-restore
mise exec -- dotnet build CaptionStudio/CaptionStudio.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=false -warnaserror --no-restore
mise exec -- dotnet test CaptionStudio.Tests/CaptionStudio.Tests.csproj -c Release -p:TreatWarningsAsErrors=true --no-restore
```

実機での UI 検証では、WinApp CLI でウィンドウと要素を調べます。日本語音声を再生して、字幕の更新、停止後の表示、保存した TXT の内容を確認します。

```powershell
mise exec -- winapp run CaptionStudio/CaptionStudio.csproj --arch x64 --detach
winapp ui list-windows -a CaptionStudio
winapp ui inspect -a CaptionStudio --depth 8
winapp ui invoke StartCaptureButton -a CaptionStudio
winapp ui get-value CaptureStatus -a CaptionStudio
winapp ui get-value TranscriptText -a CaptionStudio
winapp ui invoke StopCaptureButton -a CaptionStudio
winapp ui invoke SaveTranscriptButton -a CaptionStudio
```

アプリは `CaptionStudio/`、字幕更新のテストは `CaptionStudio.Tests/`、CI は `.github/workflows/ci.yml` にあります。

## 参考にした実装

- 字幕ウィンドウと字幕要素の UI Automation プロパティ。 [SaveLiveCaptions](https://github.com/LiveCaptionsHelper/SaveLiveCaptions/blob/main/src/function/texthook.py)
- 表示範囲の移動に伴う文字列の重なりの扱い。 [LivecapSaver](https://github.com/SorMaze/LivecapSaver/blob/main/src/capture.rs)
- 取得開始・停止とプレビューの操作を比較。 [Live-Caption-Saver](https://github.com/Tessie27/Live-Caption-Saver)
