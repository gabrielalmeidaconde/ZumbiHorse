// Copiar/colar e arquivos de fase na versão de navegador (usado por Share.cs).
mergeInto(LibraryManager.library, {
  ZhCopy: function (ptr) {
    var text = UTF8ToString(ptr);
    var ask = function () { window.prompt("Copie o código do desafio:", text); };
    if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).catch(ask);
    else ask();
  },

  // ler o clipboard pede permissão em cada navegador de um jeito; a caixa de texto funciona em todos
  ZhPaste: function () {
    var text = window.prompt("Cole o código do desafio:") || "";
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  },

  ZhDownload: function (namePtr, textPtr) {
    var blob = new Blob([UTF8ToString(textPtr)], { type: "text/plain" });
    var a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = UTF8ToString(namePtr);
    document.body.appendChild(a);
    a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  },

  ZhPickFile: function (objectPtr, methodPtr) {
    var objectName = UTF8ToString(objectPtr), method = UTF8ToString(methodPtr);
    var input = document.createElement("input");
    input.type = "file";
    input.accept = ".txt,text/plain";
    input.onchange = function () {
      var file = input.files && input.files[0];
      if (!file) return;
      file.text().then(function (text) { SendMessage(objectName, method, text); });
    };
    input.click();
  },
});
