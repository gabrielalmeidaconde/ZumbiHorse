using UnityEngine;

/// <summary>
/// Garante que o jogo rode em qualquer cena: se não houver um GameManager na cena aberta,
/// cria um ao apertar Play. (A cena Assets/Scenes/Jogo.unity já tem um.)
/// </summary>
public static class Bootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (Object.FindAnyObjectByType<GameManager>() != null) return;
        new GameObject("Jogo").AddComponent<GameManager>();
    }
}
