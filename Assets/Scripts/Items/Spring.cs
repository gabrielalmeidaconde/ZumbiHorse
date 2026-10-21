using UnityEngine;

/// <summary>Mola: arremessa o zumbi na direção para onde aponta.</summary>
public class Spring : MonoBehaviour
{
    public SpriteRenderer Sprite;
    float compressedTimer;

    public Vector2 Direction => transform.up;

    /// <summary>Velocidade aplicada ao zumbi.</summary>
    public Vector2 Launch()
    {
        compressedTimer = 0.18f;
        if (Sprite != null) Sprite.sprite = Art.Spring(true);
        Sfx.PlayVar("spring");
        Vector2 d = Direction;
        if (Mathf.Abs(d.y) > 0.5f) return d * 22f;
        return new Vector2(d.x * 17f, 7f);
    }

    void Update()
    {
        if (compressedTimer <= 0f) return;
        compressedTimer -= Time.deltaTime;
        if (compressedTimer <= 0f && Sprite != null) Sprite.sprite = Art.Spring(false);
    }
}
