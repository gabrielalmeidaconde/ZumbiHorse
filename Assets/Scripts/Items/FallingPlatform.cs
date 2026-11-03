using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Item "Plataforma Frágil": plataforma de 3 que treme quando alguém pisa e despenca meio segundo depois,
/// caindo até bater em algo (ou sair da fase). Caindo rápido em cima de alguém, arranca uma parte.
/// </summary>
public class FallingPlatform : PhysicsBlock
{
    const float ShakeTime = 0.5f;

    enum State { Still, Shaking, Falling }
    State state;
    float timer;

    protected override void ResetBody()
    {
        base.ResetBody();
        state = State.Still;
    }

    public override void Stood(Zombie z)
    {
        if (!running || Gone || state != State.Still) return;
        state = State.Shaking;
        timer = ShakeTime;
        Sfx.PlayVar("creak", 0.7f);
    }

    void FixedUpdate()
    {
        if (!running || Gone) return;
        float dt = Time.fixedDeltaTime;
        if (state == State.Shaking)
        {
            timer -= dt;
            if (timer <= 0f)
            {
                state = State.Falling;
                Fx.Burst(pos + Offset, new Color(0.55f, 0.5f, 0.45f), 8, 3f, 0.12f);
            }
        }
        else if (state == State.Falling)
        {
            float hit = FallStep(ref pos, ref vel, dt);
            if (hit > 4f) Landed(hit);
        }
        Finish(dt);
    }

    void Update()
    {
        if (Visual == null) return;
        Visual.localPosition = state == State.Shaking && running && !Gone
            ? VisualHome + Random.insideUnitCircle * 0.05f
            : (Vector3)VisualHome;
    }
}
