using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class AttachBridgeEditor
{
    [MenuItem("Jigsaw/Attach Web Bridge")]
    public static void AttachWebBridge()
    {
        var pm = GameObject.FindAnyObjectByType<PuzzleManager>();
        var psm = GameObject.FindAnyObjectByType<PuzzleSelectionManager>();
        if (pm == null)
        {
            Debug.LogError("PuzzleManager not found in scene");
            return;
        }

        var bridge = pm.gameObject.GetComponent<JigsawWebBridge>();
        if (bridge == null)
        {
            bridge = pm.gameObject.AddComponent<JigsawWebBridge>();
        }

        bridge.puzzleManager = pm;
        bridge.selectionManager = psm;

        EditorUtility.SetDirty(pm.gameObject);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Successfully attached JigsawWebBridge and configured references in Editor!");
    }
}
