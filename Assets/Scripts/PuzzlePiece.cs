using UnityEngine;

public class PuzzlePiece : MonoBehaviour
{
    private Vector3 offset;
    public Vector2 targetPosition;
    public float snapThreshold = 0.5f;

    public PuzzleManager manager;
    private Renderer pieceRenderer;
    public bool isLocked = false;

    // アウトラインの各辺（ベベル表現用）
    [HideInInspector] public GameObject edgeL, edgeT, edgeR, edgeB;
    // 各辺のベースカラー（画像からサンプリングしたもの：L, T, R, B）
    [HideInInspector] public Color[] edgeBaseColors = new Color[4];

    void Start()
    {
        pieceRenderer = GetComponent<Renderer>();
    }

    public void BeginDrag(Vector3 mousePosition)
    {
        if (isLocked) return;
        
        // グループ全体のルート位置とのオフセットを計算
        offset = transform.root.position - mousePosition;
        
        manager.BringToFront(transform.root);
    }

    public void OnPointerDrag(Vector3 mousePosition)
    {
        if (isLocked) return;

        Vector3 newRootPos = new Vector3(mousePosition.x + offset.x, mousePosition.y + offset.y, 0);
        
        // 画面外へのドラッグを防止するため、クリックしたピース自体が画面内に収まるように制限
        float camHeight = Camera.main.orthographicSize;
        float camWidth = camHeight * Camera.main.aspect;
        
        // 新しいルート位置に移動した場合の、このピースのグローバル座標を計算
        Vector3 newPiecePos = transform.position + (newRootPos - transform.root.position);
        
        // はみ出さないようにクランプ (少し余裕を持たせる)
        float padding = 0.5f;
        float clampedX = Mathf.Clamp(newPiecePos.x, -camWidth + padding, camWidth - padding);
        float clampedY = Mathf.Clamp(newPiecePos.y, -camHeight + padding, camHeight - padding);
        
        // クランプされた座標にピースが来るように、親（ルート）の位置を再計算
        Vector3 clampedPiecePos = new Vector3(clampedX, clampedY, 0);
        Vector3 diff = clampedPiecePos - newPiecePos;
        
        transform.root.position = newRootPos + diff;
    }

    public void EndDrag()
    {
        if (isLocked) return;
        
        // 他のピース（グループ）と結合できるかチェック
        manager.CheckForGroupSnap(this);
    }

    private bool isRotating = false;

    public void RotateGroup()
    {
        if (isLocked || isRotating) return;
        StartCoroutine(RotateCoroutine());
    }

    private System.Collections.IEnumerator RotateCoroutine()
    {
        isRotating = true;
        Transform root = transform.root;

        // 1. グループ全体の境界（Bounds）の中心を計算
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds bounds = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) bounds.Encapsulate(rs[i].bounds);
            Vector3 center = bounds.center;

            // 2. 0.1秒かけて90度回転させるアニメーション
            float duration = 0.1f;
            float elapsed = 0f;
            float lastAngle = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float currentAngle = Mathf.SmoothStep(0, -90f, t);
                float delta = currentAngle - lastAngle;
                
                root.RotateAround(center, Vector3.forward, delta);
                lastAngle = currentAngle;
                yield return null;
            }

            // 3. 誤差を補正
            float finalZ = Mathf.Round(root.eulerAngles.z / 90f) * 90f;
            root.rotation = Quaternion.Euler(0, 0, finalZ);
            
            manager.CheckForGroupSnap(this);
        }

        isRotating = false;
    }
}