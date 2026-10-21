using UnityEngine;

/// <summary>Serra que vai e volta sobre um trilho (item "Serra Móvel" e serras dos mapas).</summary>
public class MovingSaw : MonoBehaviour
{
    public Vector2 LocalA, LocalB;
    public float Speed = 2.5f;
    public Transform Blade;

    float t;
    int dir = 1;

    /// <summary>Cria trilho + lâmina como filhos de parent, indo de a até b (coordenadas locais).</summary>
    public static MovingSaw Build(GameObject parent, Vector2 a, Vector2 b, float speed, float startT = 0f)
    {
        var ms = parent.AddComponent<MovingSaw>();
        ms.LocalA = a;
        ms.LocalB = b;
        ms.Speed = speed;
        ms.t = startT;

        Vector2 d = b - a;
        int cells = Mathf.RoundToInt(Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y))) + 1;
        var rail = new GameObject("Trilho da serra");
        rail.transform.SetParent(parent.transform, false);
        rail.transform.localPosition = (a + b) / 2f;
        if (Mathf.Abs(d.y) > Mathf.Abs(d.x)) rail.transform.localRotation = Quaternion.Euler(0, 0, 90f);
        var rsr = rail.AddComponent<SpriteRenderer>();
        rsr.sprite = Art.SawRail(cells);
        rsr.sortingOrder = 4;

        var blade = new GameObject("Serra");
        blade.transform.SetParent(parent.transform, false);
        blade.transform.localPosition = a;
        var cc = blade.AddComponent<CircleCollider2D>();
        cc.isTrigger = true;
        cc.radius = 0.45f;
        blade.AddComponent<Hazard>();
        var vis = new GameObject("Lamina");
        vis.transform.SetParent(blade.transform, false);
        var sr = vis.AddComponent<SpriteRenderer>();
        sr.sprite = Art.Saw;
        sr.sortingOrder = 6;
        vis.AddComponent<Spinner>().DegreesPerSecond = -480f;
        ms.Blade = blade.transform;
        return ms;
    }

    public const float BladeRadius = 0.45f;

    /// <summary>Onde a lâmina vai estar daqui a 'ahead' segundos (para os bots desviarem).</summary>
    public Vector2 PredictWorld(float ahead)
    {
        float len = Vector2.Distance(LocalA, LocalB);
        if (len < 0.01f || Blade == null) return Blade != null ? (Vector2)Blade.position : (Vector2)transform.position;
        float s = Mathf.PingPong(t + dir * Speed * ahead / len, 1f);
        float e = s * s * (3f - 2f * s);
        return transform.TransformPoint(Vector2.Lerp(LocalA, LocalB, e));
    }

    void FixedUpdate()
    {
        if (Blade == null) return;
        float len = Vector2.Distance(LocalA, LocalB);
        if (len < 0.01f) return;
        t += dir * Speed * Time.fixedDeltaTime / len;
        if (t >= 1f) { t = 1f; dir = -1; }
        else if (t <= 0f) { t = 0f; dir = 1; }
        float e = t * t * (3f - 2f * t);
        Blade.localPosition = Vector2.Lerp(LocalA, LocalB, e);
    }
}
