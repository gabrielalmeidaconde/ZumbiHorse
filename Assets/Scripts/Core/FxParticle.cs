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
