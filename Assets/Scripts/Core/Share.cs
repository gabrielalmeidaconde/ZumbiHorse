using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Copiar, colar, baixar e abrir arquivo de texto. No navegador usa a página (o clipboard do Unity não chega no sistema).</summary>
public static class Share
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void ZhCopy(string text);
    [DllImport("__Internal")] static extern string ZhPaste();
    [DllImport("__Internal")] static extern void ZhDownload(string fileName, string text);
    [DllImport("__Internal")] static extern void ZhPickFile(string objectName, string method);

    public static bool Browser => true;
    public static void Copy(string text) => ZhCopy(text);
    public static string Paste() => ZhPaste();
    public static void Download(string fileName, string text) => ZhDownload(fileName, text);
    /// <summary>Abre o seletor de arquivo; o texto chega depois por SendMessage(objectName, method, texto).</summary>
    public static void PickFile(string objectName, string method) => ZhPickFile(objectName, method);
#else
    public static bool Browser => false;
    public static void Copy(string text) => GUIUtility.systemCopyBuffer = text;
    public static string Paste() => GUIUtility.systemCopyBuffer;
    public static void Download(string fileName, string text) { }
    public static void PickFile(string objectName, string method) { }
#endif
}
