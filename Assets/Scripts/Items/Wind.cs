using UnityEngine;

/// <summary>Área de vento do ventilador: empurra os zumbis na direção para onde aponta (transform.up).</summary>
public class Wind : MonoBehaviour
{
    public const float Length = 4.5f;
    public float Strength = 80f;   // aceleração na vertical
    public float MaxSpeed = 10f;
    public float SideSpeed = 6f;   // velocidade extra quando sopra de lado
    public ItemInstance Item;

    float streakTimer;

    public Vector2 Direction => transform.up;

    void Update()
    {
        if (Item == null || !Item.Placed) return;
        streakTimer -= Time.deltaTime;
        if (streakTimer > 0f) return;
        streakTimer = 0.12f;
        // risquinhos de vento
        Vector2 basePos = (Vector2)Item.transform.position + Direction * 0.6f + (Vector2)Item.transform.right * Random.Range(-0.4f, 0.4f);
        var p = Fx.Spawn(Art.Pixel, basePos, new Color(0.85f, 0.95f, 1f, 0.5f), 0.07f, 3);
        p.transform.localScale = new Vector3(0.06f, 0.3f, 1f);
        p.transform.up = Direction;
        p.Velocity = Direction * 8f;
        p.Gravity = 0f;
        p.Life = Length / 8f;
        p.Shrink = false;
    }
}
