using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Item "Pedregulho": pedra redonda que rola para o lado escolhido durante a corrida, cai das beiradas e volta
/// quando bate numa parede. Quem encostar perde uma parte (o ponto vai para quem colocou). No ácido, afunda.
/// Guarda o caminho dos próximos segundos para os bots desviarem (como as serras móveis).
/// </summary>
public class Boulder : PhysicsBlock
{
    public const float Speed = 3.4f, Radius = 0.42f;
    const float TrackStep = 0.05f, TrackTime = 4f;

    public SpriteRenderer Arrow;

    protected override bool LandsOnZombies => false; // passa por dentro deles (e machuca)

    int dir = 1, startDir = 1;
    bool stuck;                 // preso num buraco do tamanho dele: para de rolar
    float lastBonk = -1f;
    float trackTimer, rollTimer, bonkTimer, trackBuiltAt;
    readonly List<Vector2> track = new List<Vector2>();
    static readonly List<Collider2D> overlaps = new List<Collider2D>();
    static readonly ContactFilter2D triggerFilter = new ContactFilter2D { useTriggers = true };

    /// <summary>Rotação 0 rola para a direita, 2 para a esquerda (o item só espelha).</summary>
    public void SetDirection(int rotation)
    {
        startDir = rotation == 2 ? -1 : 1;
        dir = startDir;
        if (Arrow != null) Arrow.sprite = Art.Arrow(startDir > 0 ? 3 : 1);
    }

    protected override void ResetBody()
    {
        base.ResetBody();
        dir = startDir;
        stuck = false;
        lastBonk = -1f;
        track.Clear();
        trackTimer = 0f;
    }

    void FixedUpdate()
    {
        if (!running || Gone) return;
        float dt = Time.fixedDeltaTime;
        PopOut();
        bool grounded = Step(ref pos, ref vel, ref dir, dt, out bool bonk, out float hit, stuck);
        if (bonk)
        {
            // bateu nas duas paredes em seguida: está preso, para de rolar
            if (grounded && Time.fixedTime - lastBonk < 0.3f) stuck = true;
            lastBonk = Time.fixedTime;
        }
        bonkTimer -= dt;
        rollTimer -= dt;
        if (bonk && bonkTimer <= 0f)
        {
            bonkTimer = 0.2f;
            Sfx.PlayVar("bonk", 0.6f);
            Fx.Dust(pos + new Vector2(-dir * Radius, -0.2f), 3);
        }
        if (hit > 4f) Landed(hit);
        if (grounded && !stuck && rollTimer <= 0f)
        {
            rollTimer = 0.3f;
            Sfx.PlayVar("roll", 0.3f);
        }
        if (Visual != null && !stuck) Visual.Rotate(0f, 0f, -dir * Speed * dt / Radius * Mathf.Rad2Deg);

        if (InAcid(pos))
        {
            Fx.Burst(pos, new Color(0.4f, 0.95f, 0.3f), 14, 6f, 0.16f);
            Sfx.PlayVar("bonk", 0.4f);
            Vanish();
            return;
        }
        Finish(dt);

        trackTimer -= dt;
        if (trackTimer <= 0f) { trackTimer = 0.25f; RebuildTrack(); }
    }

    /// <summary>Um passo do movimento: cai, rola e vira na parede. Devolve se está no chão.</summary>
    bool Step(ref Vector2 p, ref Vector2 v, ref int d, float dt, out bool bonk, out float hit, bool still = false)
    {
        bonk = false;
        hit = FallStep(ref p, ref v, dt);
        bool grounded = v.y == 0f;
        if (still) { v.x = 0f; return grounded; }
        float want = Speed * dt;
        float free = FreeDistance(p + Offset, CastSize(false), new Vector2(d, 0f), want);
        if (free < want - 0.0001f)
        {
            p.x += d * free;
            d = -d;
            bonk = true;
        }
        else p.x += d * want;
        v.x = d * Speed;
        return grounded;
    }

    /// <summary>
    /// O pedregulho não é sólido, então um caixote (ou plataforma) pode cair em cima dele e ficar sobreposto:
    /// nesse caso ele sobe para cima do bloco e volta a rolar (senão bateria "parede" dos dois lados e ficaria preso).
    /// </summary>
    void PopOut()
    {
        overlaps.Clear();
        Physics2D.OverlapBox(pos + Offset, CastSize(true), 0f, solidFilter, overlaps);
        float top = float.MinValue;
        foreach (var c in overlaps)
            if (!Ignore(c, false)) top = Mathf.Max(top, c.bounds.max.y);
        if (top == float.MinValue) return;
        pos.y = top + Size.y / 2f - Offset.y + 0.01f;
        vel.y = 0f;
        stuck = false;
    }

    static bool InAcid(Vector2 p)
    {
        overlaps.Clear();
        Physics2D.OverlapCircle(p, 0.15f, triggerFilter, overlaps);
        foreach (var c in overlaps)
        {
            var h = c.GetComponent<Hazard>();
            if (h != null && h.Deadly) return true;
        }
        return false;
    }

    void RebuildTrack()
    {
        track.Clear();
        trackBuiltAt = Time.fixedTime;
        Vector2 p = pos, v = vel;
        int d = dir;
        track.Add(p);
        for (float t = 0f; t < TrackTime; t += TrackStep)
        {
            Step(ref p, ref v, ref d, TrackStep, out _, out _, stuck);
            if (p.y < Level.KillY - 1f || InAcid(p)) break;
            track.Add(p);
        }
    }

    /// <summary>Onde o pedregulho vai estar daqui a 'ahead' segundos (bem longe se ele já sumiu).</summary>
    public Vector2 PredictWorld(float ahead)
    {
        if (!running || Gone) return new Vector2(9999f, 9999f);
        if (track.Count == 0) return pos;
        float f = (ahead + Time.fixedTime - trackBuiltAt) / TrackStep; // o caminho foi calculado há pouco
        int i = Mathf.FloorToInt(f);
        if (i >= track.Count - 1) return track.Count >= TrackTime / TrackStep ? track[track.Count - 1] : new Vector2(9999f, 9999f);
        return Vector2.Lerp(track[Mathf.Max(0, i)], track[i + 1], f - i);
    }
}
