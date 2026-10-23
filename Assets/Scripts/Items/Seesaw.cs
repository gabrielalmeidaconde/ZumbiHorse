using UnityEngine;

/// <summary>
/// Item "Gangorra": prancha de 5 sobre um apoio. Cada zumbi em cima faz peso (quanto mais longe do meio, mais):
/// o lado mais pesado desce e o outro sobe, empurrando para cima (ou arremessando) quem estiver lá.
/// Sem ninguém, ela volta devagar para o meio.
/// </summary>
public class Seesaw : RunBehaviour
{
    public const float MaxAngle = 22f;
    const float Weight = 260f, Spring = 6f, Damping = 2.5f;

    public Rigidbody2D Plank;

    float angle, angVel, maxSeen;
    bool running, logged;

    public override void OnRunStart() { Flatten(); running = true; logged = false; maxSeen = 0f; }

    public override void OnRunEnd()
    {
        if (DemoDriver.Active && running) Debug.Log($"[Demo] gangorra em {Plank.position}: inclinou no máximo {maxSeen:0}°");
        running = false;
        Flatten();
    }

    void Flatten()
    {
        angle = angVel = 0f;
        Plank.transform.localRotation = Quaternion.identity;
    }

    void FixedUpdate()
    {
        if (!running) return;
        float dt = Time.fixedDeltaTime;
        float torque = 0f;
        foreach (var z in Zombie.All)
            if (z != null && z.State == ZombieState.Alive && z.GroundSeesaw == this)
                torque += Mathf.Clamp(z.Position.x - Plank.position.x, -2.5f, 2.5f);

        // peso à direita (torque > 0) gira no sentido horário, ou seja, ângulo negativo
        angVel += (-torque * Weight - angle * Spring - angVel * Damping) * dt;
        angle += angVel * dt;
        if (angle > MaxAngle) { angle = MaxAngle; if (angVel > 0f) angVel = 0f; }
        else if (angle < -MaxAngle) { angle = -MaxAngle; if (angVel < 0f) angVel = 0f; }
        maxSeen = Mathf.Max(maxSeen, Mathf.Abs(angle));
        if (DemoDriver.Active && !logged && Mathf.Abs(angle) >= MaxAngle)
        {
            logged = true;
            Debug.Log($"[Demo] gangorra em {Plank.position} desceu para a {(angle < 0f ? "direita" : "esquerda")}");
        }
        Plank.MoveRotation(transform.eulerAngles.z + angle);
    }
}
