# v1.0.0 - 装飾モザイク for YMM4

YukkuriMovieMaker4向けの装飾モザイクエフェクトプラグインの初回リリースです。
素材のエッジに沿う方向場を求め、Hausnerの装飾モザイク手法で四角タイルを敷き詰め、ローマ風モザイクとして描画します。
タイルの配置はシードから決定論的に決まり、敷設のパラメータでタイルが順に敷かれていく遷移を表現できます。
計算はComputeSharpの計算シェーダーがDirect3D 12で実行し、YMM4のDirect3D 11側とは共有テクスチャおよび共有フェンスで接続します。
8言語のリソース構成のUIを備えます。

---

## 新機能

### 1. 装飾モザイクの計算パイプライン

`DecorativeMosaicPipeline`は、解析、配置、描画の3段階の計算シェーダーを`ComputeContext`へ記録して実行します。計算格子のバッファーは計算領域の大きさと品質とタイルの間隔に応じて確保し、サイズが変わらないフレームでは再利用します。処理の流れは次のとおりです。

1. `AnalyzeShader`が、各格子セルに対応する画素のアルファ値と輝度を調べ、しきい値`0.05`を超えるセルを素材の範囲として記録します。
2. `EdgeSeedShader`が、素材の範囲の境界セルと、輝度の勾配がしきい値を超える内部のセルをエッジとして置き、`EdgeJumpFloodPassShader`が格子解像度のジャンプフラッドで各セルから最も近いエッジを求めます。
3. `DirectionShader`が、最近傍のエッジへの変位に直交する接線方向から方向場を作り、エッジからの距離とあわせて記録します。
4. `SpawnShader`が、セル位置とシードのハッシュによる棄却サンプリングでタイルの初期位置を置きます。
5. `SiteSeedShader`と`SiteJumpFloodPassShader`と`CentroidAccumulateShader`と`SiteMoveShader`が、Lloyd緩和の1反復を実行し、品質が定める回数だけ記録を繰り返します。
6. `RenderShader`が、画素ごとに所属するボロノイ領域のタイルを求め、タイル、目地、陰影、色むらを計算して出力します。

| シェーダー | 役割 |
|---|---|
| `AnalyzeShader` | 画素のアルファ値と輝度から素材の範囲を求める |
| `MaskHashShader` | 素材の範囲と輝度のハッシュを集計する |
| `EdgeSeedShader` | 輪郭と輝度エッジのセルを置く |
| `EdgeJumpFloodPassShader` | 最近傍のエッジを求める |
| `DirectionShader` | 方向場とエッジからの距離を求める |
| `SpawnShader` | タイルの初期位置を置く |
| `SiteSeedShader` / `SiteJumpFloodPassShader` | 方向つきマンハッタン計量のボロノイ図を求める |
| `CentroidAccumulateShader` | ボロノイ領域の重心を集計する |
| `SiteMoveShader` | タイルを重心へ移動する |
| `RenderShader` | タイルと目地を描画する |

### 2. Hausner手法によるタイル配置

配置の計算は、Hausnerの論文「Simulating Decorative Mosaics」（SIGGRAPH 2001）に基づきます。方向場は、エッジからのユークリッド距離場の勾配に直交する接線方向です。タイルの位置は、主軸を方向場へ局所回転させたマンハッタン計量`|u|/γ + |v|·γ`の重心ボロノイ図をLloyd反復で解いて求めます。マンハッタン計量の重心ボロノイ図は局所的に正方格子を作るため、曲がる格子に四角タイルが密に並びます。

1反復の更新は次の順で行います。

- ボロノイ: 各タイルの位置を種としてジャンプフラッドを実行し、各セルが最も近いタイルを求めます。距離は方向つきマンハッタン計量で評価し、細部による縮小率で割ります。論文のz-bufferによるボロノイ計算は、同一機能のジャンプフラッド法へ置き換えています。
- 重心: 素材の範囲のセルを、所属するタイルごとに固定小数点の整数`InterlockedAdd`で集計します。反復の大部分では、エッジからの距離が`0.35×間隔`未満のセルを集計から除外し、タイルをエッジから遠ざけます。最後の3反復では除外を解いて隙間を詰めます。これは論文6節のエッジ回避と同じ手順です。
- 移動: 各タイルを自身の領域の重心へ移動し、移動先の方向場と縮小率を反映します。領域を持たないタイルは削除します。

整列のパラメータは、方向場の角度を4倍角の空間で無回転と合成する度合いです。細長さのパラメータは計量の異方性`γ`を1〜2.5へ割り当て、細部のパラメータはエッジ近傍のタイルを最小0.5倍まで縮小します。乱数は使用せず、すべての値が入力から決定論的に決まります。

### 3. 可視範囲への出力矩形の最小化

配置の完了後、タイルの位置と敷設順位の記録をCPUへ読み戻します。読み戻す量は格子3面の値だけで、画素は読み戻しません。敷設順位ごとの境界を累積した配列を1回の走査で構築するため、敷設が定める可視タイルのバウンディングボックスはフレームごとに一定時間で求まります。

- 矩形にはタイルの半径分の余白を加え、4画素境界へそろえます。
- 出力テクスチャは矩形の大きさで確保し、Direct2Dの`Crop`と`AffineTransform2D`で元の位置へ合成します。
- モザイクは素材の不透明な範囲の内側だけに描画されるため、計算領域は素材の範囲そのままで余白を持ちません。

### 4. 構造キャッシュ

タイルの配置を決める入力が変わらないフレームでは、配置の計算を再利用します。

- 素材の範囲と輝度は、解析の計算後にセル位置と量子化した輝度のハッシュの総和とXORの2値へ集約し、8個の整数の読み戻しで前フレームと比較します。
- ハッシュ、計算領域の大きさ、品質、シード、大きさ、細長さ、整列、エッジ検出、細部が一致する場合は、配置段階と記録の読み戻しを実行しません。
- 構造が同じで、敷設、目地幅、乱れ、立体感、色むら、目地色、目地濃度、出力矩形も変わらないフレームでは、描画段階も実行せず、前フレームの出力テクスチャを使用します。

### 5. Direct3D 11・Direct3D 12相互運用

`DecorativeMosaicGpuInterop`は、YMM4のDirect3D 11・Direct2D側と、ComputeSharpのDirect3D 12側を接続します。ComputeSharpの`GraphicsDevice`は、YMM4が使うDXGIアダプターのLUIDと一致するものを選びます。

入力と出力は、ComputeSharpで確保した共有テクスチャをDirect3D 11のテクスチャとして開き、Direct2Dのビットマップとして扱います。入力のテクスチャは素材の大きさで確保し、出力のテクスチャは可視範囲の矩形を収める容量で確保して拡大時だけ作り直します。両デバイスの同期は、Direct3D 12のフェンスを共有フェンスとしてDirect3D 11側で開いて行います。

`BeginCompute`は、Direct3D 11のコマンドを送出したうえでDirect3D 12側を待機させ、`EndCompute`は、Direct3D 12側の完了をDirect3D 11側で待ちます。

Direct3D 12デバイスの取得や共有リソースの作成に失敗した場合は、`TryCreate`が`null`を返し、エフェクトを適用せず入力映像を表示します。

### 6. カスタムシェーダーによる合成

`DecorativeMosaicCustomEffect`は、`[CustomEffect(2)]`の2入力エフェクトです。入力0は元映像、入力1は描画したモザイクです。ピクセルシェーダー`DecorativeMosaic.hlsl`の`main`は、`amount`が0以下のとき元映像をそのまま返し、そうでないときはモザイクのRGBをアルファでクランプし、`mosaic + source * (1 - mosaic.a)`のアルファ合成でモザイクを元映像の上へ重ねます。タイルの色は描画段階でモザイクの画素へ焼き込むため、合成は単純な重ね合わせです。

定数バッファーは`Amount`と3つの詰め物で16バイトです。`MapInputRectsToOutputRect`は2つの入力矩形の和集合を出力矩形とします。モザイクは素材の内側だけに描画されるため、出力範囲は素材より広がりません。

シェーダーリソース: `pack://application:,,,/DecorativeMosaic;component/Shaders/DecorativeMosaic.cso`（ps_5_0、`ShaderResourceUri.Get`が生成）

### 7. エフェクト定義とパラメータ

`DecorativeMosaicEffect`は、YMM4の映像エフェクトとして宣言されます。

`[VideoEffect]`属性は以下のパラメーターで宣言されます。

- 表示名: `Texts.DecorativeMosaic`（ローカライズキー、日本語では「装飾モザイク」）
- カテゴリー: `VideoEffectCategories.Decoration`・`VideoEffectCategories.Animation`
- 検索タグ: `TagMosaic`・`TagTile`・`TagRoman`
- `IsAviUtlSupported = false`によりAviUtl向けEXO出力は非対応
- `ResourceType = typeof(Texts)`でローカライズリソースを指定

公開プロパティは以下のとおりです。基本項目は「基本」グループ、タイル項目は「タイル」グループ、方向項目は「方向」グループ、描画項目は「描画」グループに属します。

| プロパティ | 型 | デフォルト | 内部範囲 | アニメーション |
|---|---|---|---|---|
| `Amount` | `Animation` | 100 | 0〜100 | あり |
| `Laying` | `Animation` | 100 | 0〜100 | あり |
| `Quality` | `DecorativeMosaicQuality` | `High` | — | なし |
| `TileSize` | `Animation` | 14 | 2〜200 | あり |
| `Grout` | `Animation` | 30 | 0〜100 | あり |
| `Aspect` | `Animation` | 0 | 0〜100 | あり |
| `Irregularity` | `Animation` | 25 | 0〜100 | あり |
| `Detail` | `Animation` | 0 | 0〜100 | あり |
| `Seed` | `int` | 0 | 0〜int.MaxValue | なし |
| `Align` | `Animation` | 100 | 0〜100 | あり |
| `EdgeDetect` | `Animation` | 50 | 0〜100 | あり |
| `Bevel` | `Animation` | 35 | 0〜100 | あり |
| `ColorVariation` | `Animation` | 20 | 0〜100 | あり |
| `GroutColor` | `Color` | #FFD1C8BA | — | なし |
| `GroutOpacity` | `Animation` | 100 | 0〜100 | あり |

`GetAnimatables`は`Amount`・`Laying`・`TileSize`・`Grout`・`Aspect`・`Irregularity`・`Detail`・`Align`・`EdgeDetect`・`Bevel`・`ColorVariation`・`GroutOpacity`を返します。`Seed`は負値を代入すると0へ丸めます。

`CreateExoVideoFilters`は空のシーケンスを返します（EXO非対応）。`CreateVideoEffect`は映像処理用のインスタンスを生成します。エフェクトを最初に生成したときに、更新確認を一度だけ開始します。

### 8. フレームごとの更新

各フレームでYMM4の`EffectDescription`からフレーム位置、アイテム長、FPSを取得し、アニメーション値を評価します。値をパイプラインが前提とする範囲へ制限してから転送します。

| パラメータ | 変換 |
|---|---|
| `Amount` | `value / 100` をカスタムシェーダーの`Amount`へ |
| `Laying` | `value / 100` を0〜1へクランプ |
| `TileSize` | 値をそのまま2〜200画素へクランプ |
| `Grout` | `value / 100` を0〜1へクランプ |
| `Aspect` | `value / 100` を0〜1へクランプし、計量の異方性1〜2.5へ |
| `Irregularity` | `value / 100` を0〜1へクランプ |
| `Detail` | `value / 100` を0〜1へクランプ |
| `Align` | `value / 100` を0〜1へクランプ |
| `EdgeDetect` | `value / 100` を0〜1へクランプし、輝度勾配のしきい値へ |
| `Bevel` | `value / 100` を0〜1へクランプ |
| `ColorVariation` | `value / 100` を0〜1へクランプ |
| `GroutColor` | RGB各成分を0〜1へ |
| `GroutOpacity` | `value / 100` を0〜1へクランプ |
| `Seed` | 0以上へクランプ |

強さが0以下のとき、または敷設が0以下のときは、モザイクを描画せず入力映像をそのまま出力します。入力の範囲が有限でない場合や、長辺が8192画素を超える場合も、入力映像を表示します。

### 9. 品質設定

品質は、計算格子の解像度とLloyd反復の回数をまとめて切り替えます。

| 品質 | 1タイルあたりのセル数 | Lloyd反復 | 格子解像度の上限 |
|---|---:|---:|---:|
| 標準 | 5 | 10回 | 512 |
| 高品質 | 6 | 18回 | 768 |
| 最高品質 | 8 | 26回 | 1024 |

格子解像度の上限は計算領域の長辺のセル数です。セルの大きさはタイルの間隔と上限の両方から決まり、短辺のセル数は計算領域の縦横比に合わせ、最小4セルとします。

### 10. ローカライズ

`Texts`クラスは`[AutoGenLocalizer]`属性を持つ`partial`クラスとして宣言されます。
`YukkuriMovieMaker.Generator`のソースジェネレーターが`Texts.csv`を処理し、各ロケールのリソースファイルを自動生成します。

対応リソース: 日本語（`ja-jp`）・英語（`en-us`）・中国語簡体字（`zh-cn`）・中国語繁体字（`zh-tw`）・韓国語（`ko-kr`）・スペイン語（`es-es`）・アラビア語（`ar-sa`）・インドネシア語（`id-id`）

主なローカライズキーは以下のとおりです。

| キー | ja-jp |
|---|---|
| `DecorativeMosaic` | 装飾モザイク |
| `BasicGroup` | 基本 |
| `TileGroup` | タイル |
| `FlowGroup` | 方向 |
| `AppearanceGroup` | 描画 |
| `Amount` | 強さ |
| `Laying` | 敷設 |
| `Quality` | 品質 |
| `TileSize` | 大きさ |
| `Grout` | 目地幅 |
| `Aspect` | 細長さ |
| `Irregularity` | 乱れ |
| `Detail` | 細部 |
| `Seed` | シード |
| `Align` | 整列 |
| `EdgeDetect` | エッジ検出 |
| `Bevel` | 立体感 |
| `ColorVariation` | 色むら |
| `GroutColor` | 目地色 |
| `GroutOpacity` | 目地濃度 |
| `QualityBalanced` | 標準 |
| `QualityHigh` | 高品質 |
| `QualityUltra` | 最高品質 |
| `TagMosaic` | モザイク |
| `TagTile` | タイル |
| `TagRoman` | ローマ風 |
| `UpdateAvailableMessage` | 新しいバージョン {0} が公開されています。 |
