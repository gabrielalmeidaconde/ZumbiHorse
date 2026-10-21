using UnityEngine;

/// <summary>Mata o zumbi que encostar. Owner = jogador que colocou a armadilha (-1 = ninguém).</summary>
public class Hazard : MonoBehaviour
{
    public int OwnerOverride = -1;

    public int Owner => OwnerOverride >= 0 ? OwnerOverride : ItemInstance.OwnerOf(this);
}
