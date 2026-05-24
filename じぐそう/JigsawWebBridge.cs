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
    }

    /// <summary>
    /// Googleサイトなどの外部JavaScriptから呼び出されるAPIメソッド
    /// 引数フォーマット: "imageUrl,pieceCount" (例: "https://drive.google.com/uc?id=xxxx,96")
    /// </summary>
    public void StartPuzzleFromWeb(string message)
    {
        Debug.Log($"[JigsawWebBridge] 外部からリクエストを受信: {message}");
        
        string[] parts = message.Split(',');
        if (parts.Length < 1)
        {
            Debug.LogError("[JigsawWebBridge] メッセージフォーマットが不正です。");
            return;
        }

        string imageUrl = parts[0].Trim();
        int pieceCount = 96; // デフォルトピース数

        if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedCount))
        {
            pieceCount = parsedCount;
        }

        StopAllCoroutines();
        StartCoroutine(LoadImageAndStartPuzzle(imageUrl, pieceCount));
    }

    private IEnumerator LoadImageAndStartPuzzle(string url, int pieceCount)
    {
        Debug.Log($"[JigsawWebBridge] 画像ダウンロード開始: {url}");
        
        using (UnityWebRequest webRequest = UnityWebRequestTexture.GetTexture(url))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[JigsawWebBridge] 画像の読み込みに失敗しました: {webRequest.error}");
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(webRequest);
            if (texture == null)
            {
                Debug.LogError("[JigsawWebBridge] テクスチャのデコードに失敗しました。");
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
                puzzleManager.StartPuzzle(sprite, pieceCount);
            }
            else
            {
                Debug.LogError("[JigsawWebBridge] PuzzleManagerが見つかりません。");
            }
        }
    }
}
