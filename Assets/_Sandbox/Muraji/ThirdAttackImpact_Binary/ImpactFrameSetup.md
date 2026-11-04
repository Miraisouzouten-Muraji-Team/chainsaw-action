# 白黒インパクト 修正版 v2（Unity 6 / URP）

## 今回の変更

前版のカメライベント＋グローバルシェーダー変数を廃止し、専用Renderer Featureの描画パス内で閾値をMaterialへ設定します。
現在のカメラ色を読み、別の描画先へ白黒結果を書き、以降のURP処理へ渡します。同じ画像への同時読み書きは行いません。
Render GraphとCompatibility Modeの両方の実装を含みます。

「スラッシュと黒画面だけ出る」という症状だけでは、元の原因がカメラ指定・Renderer設定・合成順序・値の受け渡しのどれかまでは確定できません。
今回は旧方式への依存を除去し、パス未実行を検出できる構成へ変更しました。Unity Editorでのコンパイル・描画確認は未実施です。

## 差し替え（再生を停止して作業）

1. 既存のPlayerThirdAttackImpact.csとThirdAttackThreshold.shaderを同名ファイルで上書き。
   .metaは残し、同じクラスを別フォルダに重複追加しないでください。
2. 新規ThirdAttackImpactRendererFeature.csをAssets配下に追加。Editorフォルダには入れないでください。
3. 前回この演出用に追加したFull Screen Pass Renderer Featureを削除、またはチェックを外して無効化。
   他の演出用Featureは変更不要です。
4. ゲーム画面に使用しているUniversal Renderer Dataを選択。
   Project検索: t:UniversalRendererData
5. Add Renderer Feature → Third Attack Impact Renderer Feature を追加。
6. Threshold Shader欄へThirdAttackThreshold.shaderをドラッグ。
   自動で見つかる場合もありますが、ビルドへの同梱を確実にするため明示設定してください。
7. PlayerThirdAttackImpactのAuto Select Final CameraをON。
   Controller / Third Attack Data / Slash Spriteは以前と同じ設定です。

新しいFeatureにはMaterial・Fetch Color Buffer・Requirementsの手動設定はありません。
旧Materialはこの修正版では使用しません。
SensorPlayerControllerの変更は不要です。

## まず白黒描画だけを確認

1. Playする。
2. プレイヤーのPlayerThirdAttackImpactでPreview ThresholdをON。
3. 攻撃不要で白黒表示が維持されます。
4. Thresholdを0.1～0.9へ動かし、白黒境界が変わることを確認。
5. Preview InvertedをONにして反転を確認。
6. 確認後はPreview ThresholdをOFFにして3段目を出す。

Preview中はスラッシュ・黒画面を非表示にし、閾値調整を優先します。
Preview ThresholdはPlay中だけ変更し、通常運用時はOFFにしてください。

## 表示順と長さ

| コマ | 表示 | フレーム数項目 |
|---|---|---|
| 1 | 白黒2値化 | First Step Frames |
| 2 | 白黒反転 | Second Step Frames |
| 3 | 白黒2値化 | Third Step Frames |
| 4 | 白黒反転 | Fourth Step Frames |
| 5 | 黒背景＋白いスラッシュ | Slash Frames |
| 6 | 黒画面 | Final Black Frames |

全項目1なら6フレーム、2なら12フレーム。実際の描画フレーム基準で、ヒットストップ中も進行。
Start Delay Secondsはゲーム内時間。開始待ち中の攻撃キャンセルで演出予約を解除します。
Invert First Stepで1・3コマ目を反転から開始できます。

## Render StatusとConsole

- 「白黒描画パス実行済み: カメラ名」：URPの処理が呼ばれています。GPU出力の見た目まで保証する表示ではありません。
- 「白黒パス未実行」：Featureが使用中Rendererにない、無効になっている、Shader未設定、対象カメラ不一致など。
- 「BackBuffer」エラー：Universal Renderer DataのIntermediate TextureをAlwaysに変更して再確認。
- Shaderコンパイルエラー：ConsoleのShaderエラー全文を確認。

Previewを数フレーム維持して確認してください。未実行の警告は1回だけ出します。

## 複数カメラとRenderTexture

Auto Select Final Cameraは、Display 1へ直接出力するGameカメラのうち、スタックの最終カメラを対象にします。
MainCameraタグに依存しません。複数の独立した画面出力カメラがある場合はそれぞれ対象になり得ます。

RenderTextureに出す特定のカメラを加工する場合:
- Auto Select Final CameraをOFF。
- Impact Cameraにそのカメラを指定。
- そのカメラのRendererに専用Featureを追加。

Near/Middle/Farの合成の場合は、可能なら合成後の画面を描くカメラに適用してください。
Screen Space - OverlayのUIはカメラの後で描かれるため対象外です。
ゲーム全体がOverlay CanvasのRawImage上に表示されている場合は、RawImageへ渡すRenderTextureの生成カメラを指定するか、合成マテリアル側の対応が必要です。
複数RenderTextureをOverlay UIで別々に重ねる構成は本ファイルだけでは最終合成後の2値化になりません。構成情報が必要です。

このコンポーネントは有効なものを1つだけ使用してください。

## 確認範囲

- コードの受け渡し経路、描画先と読み取り元の分離、カメラ選択条件、無効化時の解除を静的確認。
- 6/12/可変フレーム長の境界、通常/反転の論理をローカルで確認。
- Unityのコンパイル、Render Graph Viewer、ゲームのGPU描画、実機ビルドは未確認。

参考資料:
https://docs.unity.com/en-us/engine/6000.0/manual/render-pipelines/universal-render-pipeline/customizing-urp/render-graph/write-render-pass
https://docs.unity.com/en-us/engine/6000.0/manual/render-pipelines/universal-render-pipeline/customizing-urp/render-graph/blit
