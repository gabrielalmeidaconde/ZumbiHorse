// Copiar/colar do código de fase na versão de navegador (usado por Clipboard.cs).
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
});
