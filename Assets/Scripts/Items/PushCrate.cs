using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Item "Caixote": bloco 1x1 que cai quando não tem nada embaixo e anda quando um zumbi no chão empurra.
/// Caindo rápido em cima de alguém, arranca uma parte (o ponto vai para quem colocou).
/// </summary>
public class PushCrate : PhysicsBlock
{
    public const float PushSpeed = 2.4f;

    float lastPushTime = -1f, scrapeTimer;
    bool lastPushOk;

    void FixedUpdate()
    {
        if (!running || Gone) return;
        float dt = Time.fixedDeltaTime;
        float hit = FallStep(ref pos, ref vel, dt);
        if (hit > 4f) Landed(hit);
        Finish(dt);
        scrapeTimer -= dt;
    }

    /// <summary>Um zumbi no chão empurra o caixote na direção 'dir'. Devolve true se ele andou.</summary>
    public bool TryPush(int dir)
    {
        if (!running || Gone) return false;
        if (Time.fixedTime == lastPushTime) return lastPushOk; // dois zumbis empurrando: anda uma vez só
        lastPushTime = Time.fixedTime;
        // em cima de um buraco (ou caindo): não anda de lado, deixa cair; senão ele passaria por cima de buracos de 1
        if (vel.y < 0f && Unsupported()) { lastPushOk = false; return false; }
        float want = PushSpeed * Time.fixedDeltaTime;
        float free = FreeDistance(pos + Offset, CastSize(false), new Vector2(dir, 0f), want);
        lastPushOk = free > 0.001f;
        if (!lastPushOk) return false;
        pos.x += dir * free;
        Body.MovePosition(pos);
        if (scrapeTimer <= 0f)
        {
            scrapeTimer = 0.22f;
            Sfx.PlayVar("scrape", 0.45f);
            Fx.Dust(pos + new Vector2(-dir * 0.4f, -0.5f), 2);
        }
        return true;
    }

    /// <summary>Para os bots: o caixote no lugar onde ele vai parar depois de cair.</summary>
    public override Rect NavRect(Rect current)
    {
        Vector2 home = Body.transform.position;
        float drop = FreeDistance(home + Offset, CastSize(true), Vector2.down, Level.H);
        if (drop >= Level.H - 0.01f) return current; // cai para fora da fase: tanto faz
        return new Rect(current.x, current.y - drop, current.width, current.height);
    }
}
