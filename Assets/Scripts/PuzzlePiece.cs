using UnityEngine;
using UnityEngine.Rendering;

public class PuzzlePiece : MonoBehaviour
{
    public int id;
    public Vector3 correctPos;
    public int baseOrder; // 追加：元の表示順序を記憶

    private Vector3 offset;
    public Vector2 targetPosition;
    public float snapThreshold = 0.3f; // 実行時に Manager から上書きされる

    public PuzzleManager manager;
    private Renderer pieceRenderer;
    private Renderer shadowRenderer;
    public bool isLocked = false;
    private Camera mainCam;

    [HideInInspector] public int gridX, gridY;
    private Transform rootCache;
    private Transform MyRoot {
        get {
            if (rootCache == null || rootCache.gameObject == null) rootCache = transform.root;
            return rootCache;
        }
    }

    private Transform shadowTransform;
    private const float shadowOffsetMagnitude = 0.04f;
    private static readonly Vector3 worldShadowOffset = new Vector3(0.04f, -0.04f, 0.00005f);

    void Start()
    {
        pieceRenderer = GetComponent<Renderer>();
        shadowTransform = transform.Find("Shadow");
        if (shadowTransform != null) shadowRenderer = shadowTransform.GetComponent<Renderer>();
        mainCam = Camera.main;
        UpdateShadowPosition();
    }

    public void BeginDrag(Vector3 mousePosition)
    {
        if (isLocked) return;
        
        rootCache = transform.root;
        offset = rootCache.position - mousePosition;
        
        // 掴んだ瞬間に物理的にも手前へ、かつSortingOrderを最大に
        if (manager != null) manager.BringToFront(rootCache);
        
        // ドラッグ中の演出：少し大きくし、Z座標を手前へ
        rootCache.localScale = Vector3.one * 1.05f;
        rootCache.position = new Vector3(rootCache.position.x, rootCache.position.y, -5.0f);
        
        var sg = rootCache.GetComponent<SortingGroup>();
        if (sg != null) sg.sortingOrder = 30000; // ドラッグ中は最前面
    }

    public void OnPointerDrag(Vector3 mousePosition)
    {
        if (isLocked || isRotating) return;

        Vector3 newRootPos = new Vector3(mousePosition.x + offset.x, mousePosition.y + offset.y, 0);
        float camHeight = mainCam.orthographicSize;
        float camWidth = camHeight * mainCam.aspect;
        Vector3 newPiecePos = transform.position + (newRootPos - MyRoot.position);
        
        float padding = 0.5f;
        float clampedX = Mathf.Clamp(newPiecePos.x, -camWidth + padding, camWidth - padding);
        float clampedY = Mathf.Clamp(newPiecePos.y, -camHeight + padding, camHeight - padding);
        
        Vector3 clampedPiecePos = new Vector3(clampedX, clampedY, 0);
        Vector3 diff = clampedPiecePos - newPiecePos;
        
        // ドラッグ中は十分に手前を維持
        MyRoot.position = new Vector3(newRootPos.x + diff.x, newRootPos.y + diff.y, -5.0f);
    }

    public void EndDrag()
    {
        if (isLocked) return;
        
        // スケールを元に戻す
        MyRoot.localScale = Vector3.one;

        // Z座標を通常レイヤー（SortingOrderベース）に戻す
        var sg = MyRoot.GetComponent<SortingGroup>();
        if (sg != null) sg.sortingOrder = baseOrder;
        MyRoot.position = new Vector3(MyRoot.position.x, MyRoot.position.y, -0.0001f * baseOrder);

        // 移動による微細な位置ズレをリセットしてから吸着判定へ
        manager.RealignGroup(MyRoot, this);

        manager.CheckForGroupSnap(this);
    }

    private bool isRotating = false;

    public void RotateGroup(Vector3 mousePosition)
    {
        if (isLocked || isRotating) return;
        StartCoroutine(RotateCoroutine(mousePosition));
    }

    private System.Collections.IEnumerator RotateCoroutine(Vector3 mousePosition)
    {
        isRotating = true;
        Transform root = MyRoot;

        // 回転開始時にグループ内の全ピースとレンダラーを一度だけキャッシュ
        PuzzlePiece[] piecesInGroup = root.GetComponentsInChildren<PuzzlePiece>();
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        
        if (rs.Length > 0)
        {
            Bounds bounds = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) {
                // 影やハイライトはバウンズ計算から除外して本体のみの中心を得る
                if (rs[i].name != "Shadow" && rs[i].name != "Highlight") bounds.Encapsulate(rs[i].bounds);
            }
            Vector3 center = bounds.center;

            float duration = 0.12f;
            float elapsed = 0f;
            float lastAngle = 0f;

            // ラグ対策：大規模グループ（20枚以上）の場合は回転中の動的な影更新をスキップ
            bool skipRealtimeShadows = piecesInGroup.Length > 20;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float currentAngle = Mathf.SmoothStep(0, -90f, t);
                float delta = currentAngle - lastAngle;
                
                root.RotateAround(center, Vector3.forward, delta);
                lastAngle = currentAngle;
                
                // 規模に応じて更新頻度を調整
                if (!skipRealtimeShadows) UpdateGroupVisuals(piecesInGroup);
                yield return null;
            }

            float finalZ = Mathf.Round(root.eulerAngles.z / 90f) * 90f;
            root.rotation = Quaternion.Euler(0, 0, finalZ);
            
            // 重要：回転による浮動小数点誤差をリセットし、ピース間を数学的に正しく整列
            // 掴んでいるこのピースをアンカー（基準）にしてグループ全体の座標を再計算
            manager.RealignGroup(root, this);
            
            // 回転終了後に必ず一度全ピースの影を正しい位置に同期
            UpdateGroupVisuals(piecesInGroup); 
            
            offset = root.position - mousePosition;
            manager.CheckForGroupSnap(this);
            // 結合が発生した場合、MyRootが変わっている可能性があるためキャッシュを明示的に更新
            rootCache = transform.root;
        }

        isRotating = false;
    }

    private void SetGroupSortingOrder(int order)
    {
        var pieces = MyRoot.GetComponentsInChildren<PuzzlePiece>();
        foreach (var p in pieces)
        {
            if (p.pieceRenderer != null) p.pieceRenderer.sortingOrder = order;
            if (p.shadowRenderer != null) p.shadowRenderer.sortingOrder = order - 1;
        }
    }

    private void UpdateGroupVisuals(PuzzlePiece[] pieces)
    {
        if (pieces == null) return;
        foreach(var p in pieces) 
        {
            p.UpdateShadowPosition();
        }
    }

    public void UpdateShadowPosition()
    {
        if (shadowTransform != null)
        {
            // 回転行列の計算を簡略化するため InverseTransformDirection を使用
            shadowTransform.localPosition = transform.InverseTransformDirection(worldShadowOffset);
        }
    }
}