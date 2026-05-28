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
    var data = UTF8ToString(jsonDataStr);
    if (typeof window.saveJigsawData === 'function') {
      window.saveJigsawData(slot, data);
    }
  }
});
