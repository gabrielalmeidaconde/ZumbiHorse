using System.Collections.Generic;
using UnityEngine;

/// <summary>Osso disparado pelo canhão. Some ao bater em algo sólido.</summary>
public class Projectile : MonoBehaviour
{
    public const float Speed = 8f;
    const float Radius = 0.14f;

    Vector2 dir;
    public Vector2 Direction => dir;
    Collider2D ignore;
    Rigidbody2D rb;
    float life = 6f;

    static readonly List<Collider2D> hits = new List<Collider2D>();
    public static readonly List<Projectile> All = new List<Projectile>();

    public static Projectile Spawn(Vector2 pos, Vector2 dir, int owner, Collider2D ignore)
    {
        var go = new GameObject("Osso");
        go.transform.position = pos;
        var p = go.AddComponent<Projectile>();
        p.dir = dir.normalized;
        p.ignore = ignore;

        p.rb = go.AddComponent<Rigidbody2D>();
        p.rb.bodyType = RigidbodyType2D.Kinematic;
        p.rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = Radius;
        go.AddComponent<Hazard>().OwnerOverride = owner;

        var vis = new GameObject("Visual");
        vis.transform.SetParent(go.transform, false);
        var sr = vis.AddComponent<SpriteRenderer>();
        sr.sprite = Art.Bone;
        sr.sortingOrder = 12;
        vis.AddComponent<Spinner>().DegreesPerSecond = -720f * Mathf.Sign(dir.x == 0 ? 1 : dir.x);
        return p;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public static void ClearAll()
    {
        for (int i = All.Count - 1; i >= 0; i--)
            if (All[i] != null) Destroy(All[i].gameObject);
        All.Clear();
    }

    void FixedUpdate()
    {
        life -= Time.fixedDeltaTime;
        Vector2 next = rb.position + dir * Speed * Time.fixedDeltaTime;
        rb.MovePosition(next);

        var filter = new ContactFilter2D { useTriggers = false };
        hits.Clear();
        Physics2D.OverlapCircle(next, Radius, filter, hits);
        bool blocked = false;
        foreach (var h in hits)
        {
            if (h == ignore || h.isTrigger) continue;
            if (h.GetComponentInParent<Zombie>() != null) continue; // o zumbi detecta o osso por conta própria
            blocked = true;
            break;
        }

        bool outside = Mathf.Abs(next.x) > Level.W / 2f + 1f || Mathf.Abs(next.y) > Level.H / 2f + 1f;
        if (blocked || outside || life <= 0f)
        {
            Fx.Burst(next, new Color(0.93f, 0.9f, 0.82f), 5, 4f, 0.12f);
            Destroy(gameObject);
        }
    }
}
