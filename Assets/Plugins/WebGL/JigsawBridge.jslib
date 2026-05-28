mergeInto(LibraryManager.library, {
  CloseCanvasWindow: function () {
    // WebGLキャンバスをホストするブラウザウィンドウを閉じる
    window.close();
  },
  OnPuzzleComplete: function (elapsedTime) {
    if (typeof window.onPuzzleComplete === 'function') {
      window.onPuzzleComplete(elapsedTime);
    }
  },
  SaveToBrowser: function (slotIndex, jsonDataStr) {
    var slot = slotIndex;
    console.log("[LOG 2: jslib] SaveToBrowserが呼び出されました。スロット:", slot);
    var data = UTF8ToString(jsonDataStr);
    console.log("[LOG 3: jslib] UTF8ToStringの変換結果の長さ:", data ? data.length : "null/undefined");
    if (typeof window.saveJigsawData === 'function') {
      window.saveJigsawData(slot, data);
    } else {
      console.error("[LOG 3: ERROR] window.saveJigsawData 関数がグローバルに定義されていません！");
    }
  }
});
