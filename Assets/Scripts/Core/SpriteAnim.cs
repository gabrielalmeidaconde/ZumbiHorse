using UnityEngine;

/// <summary>Troca os quadros de um SpriteRenderer em loop (ácido, esteira, portal, fogo...).</summary>
public class SpriteAnim : MonoBehaviour
{
    public Sprite[] Frames;
    public float Fps = 6f;

    SpriteRenderer sr;
    float offset;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        offset = Random.Range(0f, 1f);
    }

    void Update()
    {
        if (sr == null || Frames == null || Frames.Length == 0) return;
        sr.sprite = Frames[(int)((Time.time + offset) * Fps) % Frames.Length];
    }

    public static SpriteAnim Add(GameObject go, float fps, params Sprite[] frames)
    {
        var a = go.AddComponent<SpriteAnim>();
        a.Frames = frames;
        a.Fps = fps;
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null && frames.Length > 0) sr.sprite = frames[0];
        return a;
    }
}
