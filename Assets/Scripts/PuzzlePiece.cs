using UnityEngine;
using UnityEngine.Rendering;

public class PuzzlePiece : MonoBehaviour
{
    public int id;
    public Vector3 correctPos;
    public int baseOrder; 

    private Vector3 offset;
    public PuzzleManager manager;
    private Renderer pieceRenderer;
    private static MaterialPropertyBlock mpb;
    public bool isLocked = false;
    private Camera mainCam;

    [HideInInspector] public int gridX, gridY;
    private Transform rootCache;
    private Transform MyRoot {
        get {
            // パズル全体（transform.root）ではなく、このピースが属するクラスター（transform.parent）を対象にする
            if (rootCache == null || rootCache.gameObject == null) rootCache = transform.parent;
            return rootCache;
        }
    }

    void OnDestroy()
    {
    }

    void Start()
    {
        pieceRenderer = GetComponent<Renderer>();
        mainCam = Camera.main;
    }

    public void BeginDrag(Vector3 mousePosition)
    {
        if (isLocked) return;
        
        rootCache = transform.parent;
        offset = rootCache.position - mousePosition;
        
        // 掴んだ瞬間に物理的にも手前へ、かつSortingOrderを最大に
        if (manager != null) manager.BringToFront(rootCache);
        
        // ドラッグ中の演出：少し大きくし、Z座標を手前へ
        rootCache.localScale = Vector3.one * 1.05f;
        rootCache.position = new Vector3(rootCache.position.x, rootCache.position.y, -5.0f);

        // スケール変更後に補正
        
        // ドラッグ中のみ SortingGroup を動的に追加して最前面へ
        var sg = rootCache.gameObject.GetComponent<SortingGroup>();
        if (sg == null) sg = rootCache.gameObject.AddComponent<SortingGroup>();
        sg.sortingOrder = 30000; // ドラッグ中は最前面
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
        
        // スケール変更後に補正

        // Z座標を通常レイヤー（SortingOrderベース）に戻す
        var sg = MyRoot.GetComponent<SortingGroup>();
        if (sg != null) Destroy(sg); // ドラッグ終了時に SortingGroup を削除してバッチングを有効化

        MyRoot.position = new Vector3(MyRoot.position.x, MyRoot.position.y, 0);

        // 移動による微細な位置ズレをリセットしてから吸着判定へ
        manager.RealignGroup(MyRoot, this);

        // ドラッグ終了（置いた瞬間）に伴い、画面全体の全グループの SortingOrder を一括で綺麗に再整理・ソートし直す
        manager.UpdateAllGroupsSortingOrder();

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
                // if (!skipRealtimeShadows) UpdateGroupVisuals(piecesInGroup); // 不要
                yield return null;
            }

            float finalZ = Mathf.Round(root.eulerAngles.z / 90f) * 90f;
            root.rotation = Quaternion.Euler(0, 0, finalZ);
            
            // 重要：回転による浮動小数点誤差をリセットし、ピース間を数学的に正しく整列
            // 掴んでいるこのピースをアンカー（基準）にしてグループ全体の座標を再計算
            manager.RealignGroup(root, this);
            
            // 回転終了後に必ず一度全ピースの影を正しい位置に同期
            // UpdateGroupVisuals(piecesInGroup); // 不要
            
            offset = root.position - mousePosition;
            manager.CheckForGroupSnap(this);
            // 結合が発生した場合、親クラスター（MyRoot）が変わっている可能性があるためキャッシュを明示的に更新
            rootCache = transform.parent;
        }

        isRotating = false;
    }

    private void SetGroupSortingOrder(int order)
    {
        var pieces = MyRoot.GetComponentsInChildren<PuzzlePiece>();
        foreach (var p in pieces)
        {
            if (p.pieceRenderer != null) p.pieceRenderer.sortingOrder = order;
        }
    }

    private void UpdateGroupVisuals(PuzzlePiece[] pieces)
    {
        if (pieces == null) return;
        foreach(var p in pieces) 
        {
        }
    }

    public void UpdateShadowPosition()
    {
        // 影用のオブジェクトが存在する場合、ピースの回転に関わらず
        // ワールド空間上で常に右下に影が落ちるように位置と回転を同期する
        Transform shadow = transform.Find("Shadow");
        if (shadow != null) {
            // ピース本体の回転に完全に合わせる（メッシュの向きを同期）
            shadow.rotation = transform.rotation;
            // ワールド空間でのオフセットを維持して配置。Zは本体より僅かに奥(0.00005f)にする
            shadow.position = transform.position + new Vector3(0.06f, -0.06f, 0.00005f);
        }
    }
}