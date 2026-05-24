# Jigsawプロジェクト 現在の作業状況まとめ

HTMLでの画像・ピース数選択から、CORSエラーを完全に回避しつつ、別ウィンドウでゲーム画面をプレミアムなUIでポップアップ起動するシステムの構築が完了しています。

---

## 📅 作業完了ステータス (2026-05-23 現在)

| タスク内容 | 対象ファイル | ステータス | 備考 |
| :--- | :--- | :---: | :--- |
| **Base64直接デコードの実装** | [JigsawWebBridge.cs](file:///c:/Users/nakam/UnityProject/Jigsaw/Assets/Scripts/JigsawWebBridge.cs) | **完了** | Base64形式の画像をメモリ上で直接展開。外部URLのCORSエラーを100%回避します。従来のURL指定にもフォールバック対応。 |
| **親ウィンドウ連携 & ローカルファイル対応** | [GoogleSiteEmbed.html](file:///c:/Users/nakam/UnityProject/Jigsaw/%E3%81%98%E3%81%90%E3%81%9D%E3%81%86/GoogleSiteEmbed.html) | **完了** | `window.open` によるポップアップ起動に変更。ブラウザの `FileReader` によるローカル画像選択機能も追加し、利便性を向上。 |
| **プレミアムゲーム起動ウィンドウの新規作成** | [jigsaw_player.html (Builds内)](file:///c:/Users/nakam/UnityProject/Jigsaw/Builds/jigsaw_player.html)<br>[jigsaw_player.html (じぐそう内)](file:///c:/Users/nakam/UnityProject/Jigsaw/%E3%81%98%E3%81%90%E3%81%9D%E3%81%86/jigsaw_player.html) | **完了** | Fumi Guitar Tab Editorと同様のOutfit/Interフォント、美しいスピンローディング、上部コントロールバーを搭載したプレイヤー画面。 |
| **賢いパス自動診断ロジックの導入** | [jigsaw_player.html](file:///c:/Users/nakam/UnityProject/Jigsaw/%E3%81%98%E3%81%90%E3%81%9D%E3%81%86/jigsaw_player.html) | **完了** | 開発環境（じぐそう内）と本番配信環境（Builds内）のどちらで起動されても、Unityのローダーを自動検知して適切なパスに切り替えます。 |

---

## 🛠️ 各ファイルの変更要約

### 1. `JigsawWebBridge.cs` (Unity C#)
- 受信した文字列が `data:image/...;base64,` から始まる場合、`System.Convert.FromBase64String` を用いてバイト配列に復元。
- `Texture2D.LoadImage` で直接テクスチャにデコードし、パズルを開始するロジックを実装。

### 2. `GoogleSiteEmbed.html` (画像選択パネル)
- パズル開始時に `window.open('jigsaw_player.html', '_blank')` でゲーム画面を起動。
- 親ウィンドウ側の `window.selectedPuzzleData` に画像データ（Base64）とピース数を共有オブジェクトとして格納。
- ユーザーの手持ち画像を即座にBase64に変換して使用できる「ローカルファイル選択 (`input type="file"`)」を追加。

### 3. `jigsaw_player.html` (ゲームプレイヤー)
- Unity WebGLを画面いっぱいにレスポンシブ表示し、ロード中は回転スピナーと進捗（%）を表示する。
- ロード完了後、親ウィンドウの `window.opener.selectedPuzzleData` を取得し、`unityInstance.SendMessage` を介してUnityにデータを自動送信。

---

## 🔍 次のステップ（動作確認の手順）

AIによる自律的な動作検証は不要とされているため、お手数ですが以下の手順で手動テストをお願いいたします。

1. **選択パネルの起動**: ブラウザで [GoogleSiteEmbed.html](file:///c:/Users/nakam/UnityProject/Jigsaw/%E3%81%98%E3%81%90%E3%81%9D%E3%81%86/GoogleSiteEmbed.html) を開きます。
2. **画像の決定**: ピース数を選び、ギャラリー画像を選択するか、手持ちのローカル画像をPCから選択します。
3. **ゲーム起動の確認**: 「パズルを開始する！」を押すと、新しいタブで [jigsaw_player.html](file:///c:/Users/nakam/UnityProject/Jigsaw/%E3%81%98%E3%81%90%E3%81%9D%E3%81%86/jigsaw_player.html) が開き、ロード後に自動的にその画像でジグソーパズルが生成・開始されることを確認します。
