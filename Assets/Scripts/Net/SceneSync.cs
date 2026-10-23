using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Tipos de mensagem do multiplayer online.</summary>
public static class Msg
{
    // host → cliente
    public const byte Welcome = 1, Reject = 2, SpriteKey = 3, Objects = 4, Remove = 5, Sound = 6, Float = 7, Ui = 8,
                      Bundle = 9, Pong = 10;
    // cliente → host
    public const byte Hello = 20, Input = 21, Ping = 22;
}

/// <summary>
/// Copia a cena do host para os clientes. Tudo o que aparece na tela é um SpriteRenderer, então o host manda,
/// para cada um, só o que mudou: qual sprite (pelo nome), posição, rotação, escala, cor e ordem de desenho.
/// O cliente não roda o jogo: só desenha as cópias (veja SceneMirror).
/// </summary>
public class SceneSync
{
    public const byte MSprite = 1, MPos = 2, MRot = 4, MScale = 8, MColor = 16, MOrder = 32, MAll = 63;

    public struct State
    {
        public ushort Key;
        public short X, Y, Z, SX, SY, Order;
        public ushort Rot;
        public Color32 Color;
        public byte Flags;
    }

    readonly Dictionary<int, State> sent = new Dictionary<int, State>();
    readonly Dictionary<string, ushort> keyIds = new Dictionary<string, ushort>();
    readonly List<string> keys = new List<string>();
    readonly HashSet<int> seen = new HashSet<int>();
    readonly List<int> gone = new List<int>();

    public static short Q(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 100f), short.MinValue, short.MaxValue);
    public static float UnQ(short v) => v / 100f;
    static ushort QRot(float deg) => (ushort)Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 360f * 65535f);
    public static float UnQRot(ushort v) => v / 65535f * 360f;

    /// <summary>Lê a cena e acrescenta em 'output' as mensagens com o que mudou desde a última chamada.</summary>
    public void Capture(List<byte[]> output)
    {
        var defs = new List<byte[]>();
        var objs = new Chunker(Msg.Objects);
        seen.Clear();
        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (!sr.enabled || sr.sprite == null || !sr.gameObject.activeInHierarchy) continue;
            int id = sr.GetInstanceID();
            seen.Add(id);
            var s = Read(sr, defs);
            byte mask = sent.TryGetValue(id, out var old) ? Diff(old, s) : MAll;
            if (mask == 0) continue;
            sent[id] = s;
            objs.Add(w => Write(w, id, mask, s));
        }

        gone.Clear();
        foreach (int id in sent.Keys) if (!seen.Contains(id)) gone.Add(id);
        var removed = new Chunker(Msg.Remove);
        foreach (int id in gone)
        {
            sent.Remove(id);
            removed.Add(w => w.Write(id));
        }

        output.AddRange(defs);
        objs.Finish(output);
        removed.Finish(output);
    }

    /// <summary>Tudo o que já foi mandado (para um cliente que acabou de chegar).</summary>
    public void FullState(List<byte[]> output)
    {
        for (int i = 0; i < keys.Count; i++) output.Add(KeyDef((ushort)i, keys[i]));
        var objs = new Chunker(Msg.Objects);
        foreach (var kv in sent)
        {
            int id = kv.Key;
            var s = kv.Value;
            objs.Add(w => Write(w, id, MAll, s));
        }
        objs.Finish(output);
    }

    State Read(SpriteRenderer sr, List<byte[]> defs)
    {
        string name = sr.sprite.name;
        if (!keyIds.TryGetValue(name, out ushort key))
        {
            key = (ushort)keys.Count;
            keys.Add(name);
            keyIds[name] = key;
            defs.Add(KeyDef(key, name));
        }
        var t = sr.transform;
        Vector3 p = t.position, sc = t.lossyScale;
        return new State
        {
            Key = key,
            X = Q(p.x), Y = Q(p.y), Z = Q(p.z),
            Rot = QRot(t.eulerAngles.z),
            SX = Q(sc.x), SY = Q(sc.y),
            Color = sr.color,
            Order = (short)sr.sortingOrder,
            Flags = (byte)((sr.flipX ? 1 : 0) | (sr.flipY ? 2 : 0)),
        };
    }

    static byte[] KeyDef(ushort key, string name)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Msg.SpriteKey);
        w.Write(key);
        w.Write(name);
        return ms.ToArray();
    }

    static byte Diff(State a, State b)
    {
        byte m = 0;
        if (a.Key != b.Key) m |= MSprite;
        if (a.X != b.X || a.Y != b.Y || a.Z != b.Z) m |= MPos;
        if (a.Rot != b.Rot) m |= MRot;
        if (a.SX != b.SX || a.SY != b.SY) m |= MScale;
        if (!a.Color.Equals(b.Color)) m |= MColor;
        if (a.Order != b.Order || a.Flags != b.Flags) m |= MOrder;
        return m;
    }

    static void Write(BinaryWriter w, int id, byte mask, State s)
    {
        w.Write(id);
        w.Write(mask);
        if ((mask & MSprite) != 0) w.Write(s.Key);
        if ((mask & MPos) != 0) { w.Write(s.X); w.Write(s.Y); w.Write(s.Z); }
        if ((mask & MRot) != 0) w.Write(s.Rot);
        if ((mask & MScale) != 0) { w.Write(s.SX); w.Write(s.SY); }
        if ((mask & MColor) != 0) { w.Write(s.Color.r); w.Write(s.Color.g); w.Write(s.Color.b); w.Write(s.Color.a); }
        if ((mask & MOrder) != 0) { w.Write(s.Order); w.Write(s.Flags); }
    }

    /// <summary>Junta vários itens em mensagens de no máximo ~1 KB: [tipo][quantidade][itens...].</summary>
    class Chunker
    {
        readonly byte type;
        readonly List<byte[]> done = new List<byte[]>();
        MemoryStream ms;
        BinaryWriter w;
        ushort count;

        public Chunker(byte type) { this.type = type; }

        public void Add(System.Action<BinaryWriter> item)
        {
            if (ms == null)
            {
                ms = new MemoryStream();
                w = new BinaryWriter(ms);
                w.Write(type);
                w.Write((ushort)0);
                count = 0;
            }
            item(w);
            count++;
            if (ms.Length > NetTransport.MaxMessage - 60) Close();
        }

        void Close()
        {
            if (ms == null) return;
            var bytes = ms.ToArray();
            bytes[1] = (byte)(count & 0xFF);
            bytes[2] = (byte)(count >> 8);
            done.Add(bytes);
            ms = null;
        }

        public void Finish(List<byte[]> output)
        {
            Close();
            output.AddRange(done);
        }
    }
}

/// <summary>Lado do cliente: as cópias dos sprites do host, com o movimento suavizado entre um envio e outro.</summary>
public class SceneMirror
{
    class Obj
    {
        public Transform T;
        public SpriteRenderer Sr;
        public Vector3 Target;
        public float TargetRot;
    }

    readonly Dictionary<int, Obj> objs = new Dictionary<int, Obj>();
    readonly List<Sprite> sprites = new List<Sprite>();
    readonly HashSet<Obj> moving = new HashSet<Obj>();
    readonly List<Obj> settled = new List<Obj>();
    Transform root;

    public int Count => objs.Count;

    public void Apply(byte type, BinaryReader r)
    {
        if (type == Msg.SpriteKey)
        {
            int key = r.ReadUInt16();
            string name = r.ReadString();
            while (sprites.Count <= key) sprites.Add(null);
            sprites[key] = Art.FromKey(name);
            if (sprites[key] == null) Debug.LogWarning("[Net] sprite desconhecido: " + name);
            return;
        }
        int n = r.ReadUInt16();
        if (type == Msg.Remove)
        {
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadInt32();
                if (!objs.TryGetValue(id, out var o)) continue;
                moving.Remove(o);
                Object.Destroy(o.T.gameObject);
                objs.Remove(id);
            }
            return;
        }
        for (int i = 0; i < n; i++) ApplyObject(r);
    }

    void ApplyObject(BinaryReader r)
    {
        int id = r.ReadInt32();
        byte mask = r.ReadByte();
        bool fresh = !objs.TryGetValue(id, out var o);
        if (fresh)
        {
            if (root == null) root = new GameObject("Rede (cópia do host)").transform;
            var go = new GameObject("net");
            go.transform.SetParent(root, false);
            o = new Obj { T = go.transform, Sr = go.AddComponent<SpriteRenderer>() };
            objs[id] = o;
        }
        if ((mask & SceneSync.MSprite) != 0)
        {
            int key = r.ReadUInt16();
            o.Sr.sprite = key < sprites.Count ? sprites[key] : null;
        }
        if ((mask & SceneSync.MPos) != 0)
        {
            o.Target = new Vector3(SceneSync.UnQ(r.ReadInt16()), SceneSync.UnQ(r.ReadInt16()), SceneSync.UnQ(r.ReadInt16()));
            if (fresh) o.T.position = o.Target; else moving.Add(o);
        }
        if ((mask & SceneSync.MRot) != 0)
        {
            o.TargetRot = SceneSync.UnQRot(r.ReadUInt16());
            if (fresh) o.T.rotation = Quaternion.Euler(0f, 0f, o.TargetRot); else moving.Add(o);
        }
        if ((mask & SceneSync.MScale) != 0) o.T.localScale = new Vector3(SceneSync.UnQ(r.ReadInt16()), SceneSync.UnQ(r.ReadInt16()), 1f);
        if ((mask & SceneSync.MColor) != 0) o.Sr.color = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        if ((mask & SceneSync.MOrder) != 0)
        {
            o.Sr.sortingOrder = r.ReadInt16();
            byte f = r.ReadByte();
            o.Sr.flipX = (f & 1) != 0;
            o.Sr.flipY = (f & 2) != 0;
        }
    }

    /// <summary>Anda um pouco em direção à última posição recebida (fica liso mesmo recebendo 20 vezes por segundo).</summary>
    public void Update(float dt)
    {
        float k = 1f - Mathf.Exp(-dt * 30f);
        settled.Clear();
        foreach (var o in moving)
        {
            if (o.T == null) { settled.Add(o); continue; }
            Vector3 p = o.T.position;
            p = (o.Target - p).sqrMagnitude > 4f ? o.Target : Vector3.Lerp(p, o.Target, k); // pulo grande (portal): teleporta
            o.T.position = p;
            float z = Mathf.LerpAngle(o.T.eulerAngles.z, o.TargetRot, k);
            o.T.rotation = Quaternion.Euler(0f, 0f, z);
            if ((o.Target - p).sqrMagnitude < 1e-6f && Mathf.Abs(Mathf.DeltaAngle(z, o.TargetRot)) < 0.05f) settled.Add(o);
        }
        foreach (var o in settled) moving.Remove(o);
    }

    public void Clear()
    {
        if (root != null) Object.Destroy(root.gameObject);
        root = null;
        objs.Clear();
        moving.Clear();
        sprites.Clear();
    }
}
