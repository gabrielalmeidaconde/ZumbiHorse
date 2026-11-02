using UnityEngine;

/// <summary>Arranca a parte do zumbi que encostar (ou mata, se for a cabeça). Owner = jogador que colocou a armadilha (-1 = ninguém).</summary>
public class Hazard : MonoBehaviour
{
    public int OwnerOverride = -1;
    /// <summary>Mata na hora, sem arrancar parte (ácido: o zumbi afunda inteiro).</summary>
    public bool Deadly;
    /// <summary>Solta faíscas quando acerta (serras).</summary>
    public bool Sparks;

    public int Owner => OwnerOverride >= 0 ? OwnerOverride : ItemInstance.OwnerOf(this);
}
