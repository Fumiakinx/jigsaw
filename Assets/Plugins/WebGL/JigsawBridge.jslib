mergeInto(LibraryManager.library, {
  CloseCanvasWindow: function () {
    // WebGLキャンバスをホストするブラウザウィンドウを閉じる
    window.close();
  },
  OnPuzzleComplete: function (elapsedTime) {
    if (typeof window.onPuzzleComplete === 'function') {
      window.onPuzzleComplete(elapsedTime);
    }
  }
});
