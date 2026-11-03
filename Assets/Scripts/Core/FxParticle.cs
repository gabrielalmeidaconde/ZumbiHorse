using UnityEngine;

/// <summary>Partícula simples: voa, cai, gira e some. Usada para sangue, poeira, explosões e corpos.</summary>
public class FxParticle : MonoBehaviour
{
    public Vector2 Velocity;
    public float Gravity = 20f;
    public float Spin;
    public float Life = 0.6f;
    public float Drag;
    public bool Shrink = true;

    SpriteRenderer sr;
    float age;
    Vector3 startScale;
    Color startColor;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        startScale = transform.localScale;
        if (sr != null) startColor = sr.color;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (age >= Life) { Destroy(gameObject); return; }
        Velocity.y -= Gravity * dt;
        Velocity *= 1f / (1f + Drag * dt);
        transform.position += (Vector3)(Velocity * dt);
        transform.Rotate(0, 0, Spin * dt);
        float k = 1f - age / Life;
        if (Shrink) transform.localScale = startScale * Mathf.Lerp(0.3f, 1f, k);
        if (sr != null) sr.color = new Color(startColor.r, startColor.g, startColor.b, startColor.a * Mathf.Clamp01(k * 2f));
    }
}

public static class Fx
{
    public static FxParticle Spawn(Sprite sprite, Vector2 pos, Color color, float scale, int order)
    {
        var go = new GameObject("fx");
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return go.AddComponent<FxParticle>();
    }

    public static void Burst(Vector2 pos, Color color, int count, float speed, float size = 0.14f, float gravity = 18f, float life = 0.7f)
    {
        for (int i = 0; i < count; i++)
        {
            var p = Spawn(Art.Pixel, pos, color, size * Random.Range(0.6f, 1.3f), 40);
            float a = Random.Range(0f, Mathf.PI * 2f);
            p.Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed * Random.Range(0.4f, 1f);
            p.Gravity = gravity;
            p.Life = life * Random.Range(0.6f, 1.2f);
            p.Spin = Random.Range(-360f, 360f);
        }
    }

    public static void Dust(Vector2 pos, int count = 5)
    {
        for (int i = 0; i < count; i++)
        {
            var p = Spawn(Art.Pixel, pos + new Vector2(Random.Range(-0.25f, 0.25f), 0.05f), new Color(0.85f, 0.8f, 0.75f, 0.7f), Random.Range(0.08f, 0.16f), 30);
            p.Velocity = new Vector2(Random.Range(-2f, 2f), Random.Range(0.3f, 1.5f));
            p.Gravity = 2f;
            p.Drag = 4f;
            p.Life = Random.Range(0.25f, 0.45f);
        }
    }

    /// <summary>Respingo verde quando um zumbi cai no ácido (surfaceY = altura da superfície).</summary>
    public static void AcidSplash(Vector2 pos, float surfaceY)
    {
        Vector2 at = new Vector2(pos.x, surfaceY);
        for (int i = 0; i < 16; i++)
        {
            var p = Spawn(Art.Pixel, at + new Vector2(Random.Range(-0.3f, 0.3f), 0f), i % 3 == 0 ? new Color(0.85f, 1f, 0.4f) : new Color(0.35f, 0.95f, 0.25f), Random.Range(0.1f, 0.2f), 41);
            p.Velocity = new Vector2(Random.Range(-3f, 3f), Random.Range(5f, 10f));
            p.Gravity = 26f;
            p.Life = Random.Range(0.45f, 0.75f);
        }
        // bolhas e fumacinha subindo devagar
        for (int i = 0; i < 6; i++)
        {
            var b = Spawn(Art.Pixel, at + new Vector2(Random.Range(-0.5f, 0.5f), 0.05f), new Color(0.6f, 1f, 0.5f, 0.6f), Random.Range(0.14f, 0.24f), 41);
            b.Velocity = new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(0.6f, 1.4f));
            b.Gravity = -0.5f;
            b.Life = Random.Range(0.6f, 1f);
        }
    }

    /// <summary>Faíscas da serra: rápidas, pequenas, amarelas e laranjas, caindo.</summary>
    public static void Sparks(Vector2 pos, int count = 14)
    {
        for (int i = 0; i < count; i++)
        {
            var p = Spawn(Art.Pixel, pos, Random.value < 0.5f ? new Color(1f, 0.95f, 0.5f) : new Color(1f, 0.6f, 0.15f), Random.Range(0.06f, 0.11f), 42);
            float a = Random.Range(0f, Mathf.PI * 2f);
            p.Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f + 0.5f) * Random.Range(5f, 11f);
            p.Gravity = 22f;
            p.Drag = 1.5f;
            p.Life = Random.Range(0.2f, 0.45f);
            // risquinho na direção do voo
            p.transform.localScale = new Vector3(p.transform.localScale.x, p.transform.localScale.y * 2.5f, 1f);
            p.transform.up = p.Velocity;
        }
    }

    /// <summary>Cópia apagada do zumbi que fica para trás (rastro da mola e do vento).</summary>
    public static void Ghost(SpriteRenderer from, Color tint, int order)
    {
        var p = Spawn(from.sprite, from.transform.position, tint, 1f, order);
        p.transform.localScale = from.transform.lossyScale;
        p.transform.rotation = from.transform.rotation;
        p.GetComponent<SpriteRenderer>().flipX = from.flipX;
        p.Gravity = 0f;
        p.Life = 0.22f;
        p.Shrink = false;
    }

    /// <summary>Destaque do primeiro a chegar no cérebro: clarão dourado, anel de brilhos e confete.</summary>
    public static void FirstPlace(Vector2 pos, Color player)
    {
        var gold = new Color(1f, 0.85f, 0.25f);
        var flash = Spawn(Art.Glow, pos, new Color(1f, 0.9f, 0.45f, 0.9f), 2.6f, 45);
        flash.Gravity = 0f;
        flash.Life = 0.45f;
        flash.Shrink = false;
        // anel que se abre
        const int ring = 28;
        for (int i = 0; i < ring; i++)
        {
            float a = i * Mathf.PI * 2f / ring;
            var p = Spawn(Art.Pixel, pos, i % 2 == 0 ? gold : Color.white, 0.16f, 44);
            p.Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 9f;
            p.Gravity = 0f;
            p.Drag = 3f;
            p.Life = 0.7f;
        }
        // confete caindo, dourado e na cor do jogador
        for (int i = 0; i < 26; i++)
        {
            var c = Spawn(Art.Pixel, pos + new Vector2(Random.Range(-0.4f, 0.4f), 0.3f), i % 2 == 0 ? gold : player, Random.Range(0.12f, 0.2f), 43);
            c.transform.localScale = new Vector3(c.transform.localScale.x, c.transform.localScale.y * 0.55f, 1f);
            c.Velocity = new Vector2(Random.Range(-5f, 5f), Random.Range(7f, 13f));
            c.Gravity = 12f;
            c.Drag = 1.2f;
            c.Spin = Random.Range(-720f, 720f);
            c.Life = Random.Range(1.2f, 1.8f);
            c.Shrink = false;
        }
    }

    public static void Explosion(Vector2 pos)
    {
        Burst(pos, new Color(1f, 0.85f, 0.3f), 26, 9f, 0.22f, 4f, 0.5f);
        Burst(pos, new Color(1f, 0.45f, 0.15f), 20, 6f, 0.3f, 2f, 0.6f);
        Burst(pos, new Color(0.3f, 0.28f, 0.32f), 18, 4f, 0.35f, -2f, 0.9f);
        var flash = Spawn(Art.Glow, pos, new Color(1f, 0.9f, 0.6f, 0.9f), 1.6f, 45);
        flash.Gravity = 0;
        flash.Life = 0.25f;
        flash.Shrink = false;
    }
}
