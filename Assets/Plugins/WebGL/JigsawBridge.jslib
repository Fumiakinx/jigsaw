mergeInto(LibraryManager.library, {
  CloseCanvasWindow: function () {
    // WebGLキャンバスをホストするブラウザウィンドウを閉じる
    window.close();
  }
});
