using System.Collections.Generic;
using UnityEngine;

/// <summary>Itens que precisam reiniciar quando a corrida começa/termina (canhão, lança-chamas, caixa podre...).</summary>
public abstract class RunBehaviour : MonoBehaviour
{
    public static readonly List<RunBehaviour> All = new List<RunBehaviour>();

    protected virtual void OnEnable() => All.Add(this);
    protected virtual void OnDisable() => All.Remove(this);

    public virtual void OnRunStart() { }
    public virtual void OnRunEnd() { }

    public static void RunStarted()
    {
        foreach (var r in All.ToArray()) if (r != null) r.OnRunStart();
    }

    public static void RunEnded()
    {
        foreach (var r in All.ToArray()) if (r != null) r.OnRunEnd();
    }
}
