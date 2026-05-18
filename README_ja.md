[中文](README.md) | [English](README_en.md) | **日本語**

# gEmuera

Godot 4.6 + .NET 8.0 によるクロスプラットフォーム Emuera テキストゲームエンジンの移植版です。

Emuera は日本の eramaker 系テキストゲームの実行エンジンで、`.ERB` スクリプトファイルと `.CSV` データファイルを解析してゲームを実行します。本プロジェクトは、オリジナルの Windows Forms / GDI+ レンダリングを Godot ノードシステムに置き換え、デスクトップと Android のクロスプラットフォーム対応を実現しています。

## 特徴

- 統一された `Emuera1824+v24+EMv18+EEv55` コア
- v18 ゲーム互換；v24 / EE / EM 拡張ゲームをネイティブサポート
- `DIMF` / `FUNCTIONF` / `LOCALF` / `ARGF` / `RESULTF` 浮動小数点サポート
- `VARIADIC` 可変長引数関数、`#REF` / `#REFS` / `#REFF` 参照型引数
- `SETIMAGELAYER` / `CLEARIMAGELAYER` 画像レイヤー制御、`SETANIMETIMER` アニメーションタイマー
- `EXISTFUNCTION` lazyload オンデマンド読み込みトリガー対応
- オプションの Lazyload 高速化戦略（`lazyloading.cfg` + オンデマンド読み込み）
- Snake 互換プロファイル（同一コア上の互換設定、独立したコアではない）
- セーブ互換性：v18 セーブは読み込み可能；v24 新規セーブは旧 v18 エンジンへの互換を保証しない
- 未実装機能の明示：SafeArithmetic、スプライト反転、アニメーション一時停止/再開、Zip 圧縮セーブ等は未実装
- ERB スクリプト実行、CSV データ読み込み、SHIFT-JIS/UTF-8 エンコーディング対応
- GPU アクセラレーション ColorMatrix カラー変換（デスクトップ、キャラクター立ち絵着色）
- Godot ネイティブ `Image.BlendRect` によるスプライト合成（高速ピクセル混合）
- 固定行高レンダリングモデル、画像は Y オフセットで上方に描画（オリジナル互換）
- ノード数上限管理（最大 1000 行）、メモリ無限増加を防止
- HTML `<img>` タグインライン画像、図形描画、ボタンインタラクション対応
- デュアルスレッドアーキテクチャ：UI レンダリングとスクリプト実行を分離
- 画面入力パッド、クイックボタン、スケーリングコントロール
- 多言語対応（日本語/中国語）
- Android 画面幅自動適応レイアウト

## プラットフォーム対応

| プラットフォーム | フレームワーク | 状態 |
|------|------|------|
| Windows | .NET 8.0 + D3D12 | 利用可能 |
| Linux | .NET 8.0 | 利用可能 |
| Android | .NET 9.0 | 利用可能 |

## クイックスタート

### 必要環境

- Godot 4.6（.NET 版）
- .NET 8.0 SDK
- （Android ビルド）.NET 9.0 SDK

### ゲームファイルの配置

ゲームフォルダを以下の場所に配置してください（フォルダ名は `era` で始まる必要があります）：

- **デスクトップ**：実行ファイルと同じディレクトリ、または Godot プロジェクトの `res://` ディレクトリ
- **Android**：`/storage/emulated/0/emuera/`

ゲームフォルダ構成：

```
eraGameName/
├── csv/              # 必須 — ゲームデータ CSV ファイル
├── erb/              # 必須 — スクリプト ERB ファイル
├── resources/        # 任意 — 画像リソース (PNG, JPG, WEBP, BMP, TGA)
└── fonts/            # 任意 — 外部 TTF フォント
```

### 実行方法

1. Godot 4.6 (.NET) でプロジェクトを開く
2. ゲームフォルダを正しい場所に配置
3. プロジェクトを実行し、起動画面でゲームを選択

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────┐
│  Godot UI Layer                                         │
│  EmueraContent (VBoxContainer, 固定行高レイアウト)        │
│  EmueraImage, Button, Label, ColorRect, Inputpad        │
├─────────────────────────────────────────────────────────┤
│  Console Layer (GameView)                               │
│  EmueraConsole → ConsoleDisplayLine → parts             │
│  PrintStringBuffer, StringStyle, HtmlManager            │
├─────────────────────────────────────────────────────────┤
│  Process Layer (GameProc)                               │
│  Process → runScriptProc → Instruction execution        │
│  ErbLoader, LogicalLineParser, LabelDictionary          │
├─────────────────────────────────────────────────────────┤
│  Data Layer (GameData)                                  │
│  VariableEvaluator, ExpressionParser, GameBase          │
│  ConstantData, CharacterData, IdentifierDictionary      │
└─────────────────────────────────────────────────────────┘
```

### スレッドモデル

エンジンはデュアルスレッドアーキテクチャを採用：

- **メインスレッド**（Godot）：UI レンダリング、入力処理、GPU ワークキュー、フレームあたり最大 1 テクスチャ読み込み
- **バックグラウンドスレッド**（EmueraThread）：ERB スクリプト実行、スプライト合成、テクスチャファイル I/O

スレッド間通信：
- バックグラウンド → メイン：`GenericUtils.uiQueue`（ConcurrentQueue）
- メイン → バックグラウンド：`EmueraThread.Input()` + `ManualResetEventSlim`
- GPU ワーク：`EmueraMain.gpuQueue`（デスクトップのみ）

### レンダリングモデル

オリジナル Emuera と同じ固定行高レンダリングモデルを採用：

- 各 `ConsoleDisplayLine` は `EffectiveLineHeight` ピクセルの垂直スペースを占有（フォントメトリクス + 行間隔から計算）
- 画像は負の Y オフセット（`ypos`）で上方に描画、前の行の上に重なる
- 行コンテンツはオーバーフロー許可（`ClipContents = false`）、画像重ね合わせ効果を実現
- ノード上限 1000 行、超過時に最古の 100 行をバッチ削除
- スプライト合成は Godot ネイティブ `Image.BlendRect` を使用（C++ 実装、C# ピクセルループより大幅に高速）

### ColorMatrix カラー変換

ERB スクリプトの `GDRAWSPRITE` 7 引数版に対応、5×5 ColorMatrix でキャラクター立ち絵着色：

- GPU パス：SubViewport + canvas_item shader リアルタイムレンダリング（デスクトップのみ）
- CPU パス：ピクセル単位マトリクス乗算（Android フォールバック）
- マトリクス規約：GDI+ 形式 `cm[input][output]`、画像は自動的に RGBA8 形式に変換

## プロジェクト構成

```
gemuera-c#/
├── project.godot              # Godot プロジェクト設定
├── gemuera-c#.csproj          # .NET プロジェクトファイル
├── first_window.tscn          # ランチャーシーン
├── main.tscn                  # メインゲームシーン
├── Scripts/
│   ├── EmueraMain.cs          # Godot エントリポイント、GPU レンダリング
│   ├── EmueraThread.cs        # バックグラウンドスレッドラッパー
│   ├── EmueraContent.cs       # UI レンダラー（行レイアウト、ノード管理）
│   ├── EmueraImage.cs         # テクスチャ描画コントロール
│   ├── ColorMatrixGPU.cs      # GPU ColorMatrix shader 管理
│   ├── SpriteManager.cs       # テクスチャキャッシュ（フレーム制限付き読み込み）
│   ├── GenericUtils.cs        # エンジン↔UI ブリッジ
│   ├── FirstWindow.cs         # ランチャー（ゲームスキャン）
│   ├── Emuera/                # コア Emuera エンジン
│   │   ├── Config/            # 設定システム
│   │   ├── Content/           # 画像/リソース管理（ネイティブ BlendRect）
│   │   ├── GameData/          # データモデル、式、変数
│   │   ├── GameProc/          # スクリプト実行エンジン
│   │   └── GameView/          # コンソールエミュレーションとレンダリング
│   ├── Shaders/
│   │   └── color_matrix.gdshader
│   └── uEmuera/               # System.Drawing/Forms 互換レイヤー
├── Fonts/                     # 内蔵フォント (MS Gothic)
└── addons/                    # Godot エディタプラグイン
```

## ビルド

```bash
# デスクトップビルド
dotnet build

# Android ビルド（.NET 9.0 SDK 必要）
dotnet build -p:GodotTargetPlatform=android
```

## 謝辞

- [Emuera](http://osdn.jp/projects/emuera/) — オリジナル Windows エンジン
- [XEmuera](https://github.com/xerysherry/XEmuera) — Xamarin/SkiaSharp モバイル移植（参考実装）
- [uEmuera](https://github.com/xerysherry/uEmuera) — Unity 移植版（参考実装）

## ライセンス

本プロジェクトはオリジナル Emuera エンジンからの移植であり、その元のライセンス条項に従います。
