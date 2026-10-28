using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Copiar e colar texto. No navegador o clipboard do Unity não chega no sistema, então usa o da página.</summary>
public static class Clipboard
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void ZhCopy(string text);
    [DllImport("__Internal")] static extern string ZhPaste();

    public static void Copy(string text) => ZhCopy(text);
    public static string Paste() => ZhPaste();
#else
    public static void Copy(string text) => GUIUtility.systemCopyBuffer = text;
    public static string Paste() => GUIUtility.systemCopyBuffer;
#endif
}
