using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class JigsawWebBridge : MonoBehaviour
{
    public PuzzleManager puzzleManager;
    public PuzzleSelectionManager selectionManager;

    void Start()
    {
        if (puzzleManager == null)
        {
            puzzleManager = FindAnyObjectByType<PuzzleManager>();
        }
        if (selectionManager == null)
        {
            selectionManager = FindAnyObjectByType<PuzzleSelectionManager>();
        }

        // 🌟 Unityエディタ上での開発・テスト実行時のみ動く自動デバッグ起動処理
        #if UNITY_EDITOR
        Debug.Log("[JigsawWebBridge] Unityエディタ起動を検知しました。デバッグ用パズル（零戦・96ピース）を自動開始します。");
        Texture2D debugTex = Resources.Load<Texture2D>("PuzzleBase/zero_fighter_boxart");
        if (debugTex != null)
        {
            Sprite debugSprite = Sprite.Create(
                debugTex, 
                new Rect(0, 0, debugTex.width, debugTex.height), 
                new Vector2(0.5f, 0.5f)
            );
            debugSprite.name = "zero_fighter_boxart";

            if (puzzleManager != null)
            {
                puzzleManager.StartPuzzle(debugSprite, 96); // 96ピース固定で開始
                
                // 画像選択用UIドキュメントを非表示にする
                if (selectionManager != null && selectionManager.uiDoc != null)
                {
                    selectionManager.uiDoc.gameObject.SetActive(false);
                }
            }
        }
        else
        {
            Debug.LogError("[JigsawWebBridge] デバッグ用画像 (zero_fighter_boxart) が Resources/PuzzleBase 内で見つかりません。");
        }
        #endif
    }

    /// <summary>
    /// Googleサイトなどの外部JavaScriptから呼び出されるAPIメソッド
    /// 引数フォーマット: "imageUrl,pieceCount" (例: "https://drive.google.com/uc?id=xxxx,96")
    /// </summary>
    public void StartPuzzleFromWeb(string message)
    {
        Debug.Log($"[JigsawWebBridge] 外部からリクエストを受信しました。");
        
        // 💡 Base64のデータ内にはカンマが含まれるため（data:image/png;base64,iVBOR...）、
        // 単純なSplit(',')ではなく、一番後ろのカンマ（ピース数の手前）で分割します。
        int lastCommaIndex = message.LastIndexOf(',');
        if (lastCommaIndex == -1)
        {
            Debug.LogError("[JigsawWebBridge] メッセージフォーマットが不正です。カンマが見つかりません。");
            return;
        }

        string imageUrl = message.Substring(0, lastCommaIndex).Trim();
        string pieceCountStr = message.Substring(lastCommaIndex + 1).Trim();
        int pieceCount = 96; // デフォルトピース数

        if (int.TryParse(pieceCountStr, out int parsedCount))
        {
            pieceCount = parsedCount;
        }

        StopAllCoroutines();
        StartCoroutine(LoadImageAndStartPuzzle(imageUrl, pieceCount));
    }

    private IEnumerator LoadImageAndStartPuzzle(string urlOrBase64, int pieceCount)
    {
        Texture2D texture = null;

        if (urlOrBase64.StartsWith("data:image/") && urlOrBase64.Contains("base64,"))
        {
            Debug.Log("[JigsawWebBridge] Base64画像データのデコードを開始します。");
            try
            {
                int base64StartIndex = urlOrBase64.IndexOf("base64,") + 7;
                string base64Data = urlOrBase64.Substring(base64StartIndex);
                byte[] imageBytes = System.Convert.FromBase64String(base64Data);

                texture = new Texture2D(2, 2);
                if (!texture.LoadImage(imageBytes))
                {
                    Debug.LogError("[JigsawWebBridge] Base64データからのテクスチャデコードに失敗しました。");
                    texture = null;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[JigsawWebBridge] Base64デコード中にエラーが発生しました: {ex.Message}");
                texture = null;
            }
            yield return null;
        }
        else
        {
            Debug.Log($"[JigsawWebBridge] 画像ダウンロード開始: {urlOrBase64}");
            using (UnityWebRequest webRequest = UnityWebRequestTexture.GetTexture(urlOrBase64))
            {
                yield return webRequest.SendWebRequest();

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[JigsawWebBridge] 画像の読み込みに失敗しました: {webRequest.error}");
                    yield break;
                }

                texture = DownloadHandlerTexture.GetContent(webRequest);
            }
        }

        if (texture == null)
        {
            Debug.LogError("[JigsawWebBridge] テクスチャの取得またはデコードに失敗しました。");
            yield break;
        }

        Debug.Log($"[JigsawWebBridge] 画像ロード成功: {texture.width}x{texture.height}");

        // Texture2DからSpriteを生成
        Sprite sprite = Sprite.Create(
            texture, 
            new Rect(0, 0, texture.width, texture.height), 
            new Vector2(0.5f, 0.5f)
        );
        sprite.name = "WebLoadedImage";

        // 画像選択用UIドキュメントが開いている場合は非表示にする
        if (selectionManager != null && selectionManager.uiDoc != null)
        {
            selectionManager.uiDoc.gameObject.SetActive(false);
        }
        // パズル生成を開始
        if (puzzleManager != null)
        {
            puzzleManager.isLoadedFromWeb = true; // Web経由での通常起動であることをマーク
            puzzleManager.StartPuzzle(sprite, pieceCount);
        }
        else
        {
            Debug.LogError("[JigsawWebBridge] PuzzleManagerが見つかりません。");
        }
    }
}
