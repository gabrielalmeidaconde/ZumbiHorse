using UnityEngine;

/// <summary>Monta o GameObject de cada item (visual + colisores) a partir do tipo.</summary>
public static class ItemFactory
{
    /// <summary>variant: só usado pelos portais (0 = verde, 1 = roxo).</summary>
    public static ItemInstance Create(ItemType type, int variant = 0)
    {
        var def = ItemDefs.Get(type);
        var root = new GameObject(def.Name);
        var item = root.AddComponent<ItemInstance>();
        item.Def = def;

        switch (type)
        {
            case ItemType.Caixa:
                AddSprite(root, Art.Crate, Vector2.zero, 5);
                AddBox(root, Vector2.zero, Vector2.one);
                break;

            case ItemType.Prancha:
                AddSprite(root, Art.Plank, new Vector2(0f, 0.25f), 5);
                AddBox(root, new Vector2(0f, 0.25f), new Vector2(3f, 0.5f));
                break;

            case ItemType.Pedra:
                AddSprite(root, Art.BigStone, new Vector2(0.5f, 0.5f), 5);
                AddBox(root, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f));
                break;

            case ItemType.Espinhos:
            {
                AddSprite(root, Art.Spikes, Vector2.zero, 6);
                AddBox(root, new Vector2(0f, -0.34f), new Vector2(1f, 0.32f));
                var hz = Child(root, "Pontas", new Vector2(0f, 0.1f));
                var t = hz.AddComponent<BoxCollider2D>();
                t.isTrigger = true;
                t.size = new Vector2(0.8f, 0.55f);
                hz.AddComponent<Hazard>();
                break;
            }

            case ItemType.Serra:
            {
                var hz = Child(root, "Serra", Vector2.zero);
                var cc = hz.AddComponent<CircleCollider2D>();
                cc.isTrigger = true;
                cc.radius = 0.5f;
                hz.AddComponent<Hazard>();
                var blade = AddSprite(hz, Art.Saw, Vector2.zero, 6);
                blade.gameObject.AddComponent<Spinner>().DegreesPerSecond = -420f;
                break;
            }

            case ItemType.Canhao:
            {
                var vis = AddSprite(root, Art.Cannon, Vector2.zero, 6);
                var body = AddBox(root, Vector2.zero, Vector2.one);
                var turret = root.AddComponent<Turret>();
                turret.Item = item;
                turret.Visual = vis.transform;
                turret.Body = body;
                break;
            }

            case ItemType.Mola:
            {
                var sr = AddSprite(root, Art.Spring(false), Vector2.zero, 6);
                AddBox(root, new Vector2(0f, -0.36f), new Vector2(1f, 0.28f));
                var trig = Child(root, "Impulso", new Vector2(0f, 0.1f));
                var t = trig.AddComponent<BoxCollider2D>();
                t.isTrigger = true;
                t.size = new Vector2(0.8f, 0.6f);
                trig.AddComponent<Spring>().Sprite = sr;
                break;
            }

            case ItemType.Gelo:
                AddSprite(root, Art.Ice, new Vector2(0.5f, 0f), 5);
                AddBox(root, new Vector2(0.5f, 0f), new Vector2(2f, 1f));
                root.AddComponent<Surface>().Slippery = true;
                break;

            case ItemType.Bomba:
                AddSprite(root, Art.Bomb, Vector2.zero, 6);
                // área da explosão (3x3)
                AddPreview(root, Vector2.zero, new Vector2(3f, 3f), new Color(1f, 0.3f, 0.2f, 0.25f));
                break;

            case ItemType.SerraMovel:
                MovingSaw.Build(root, new Vector2(-1f, 0f), new Vector2(1f, 0f), 2.5f);
                break;

            case ItemType.Portal:
            {
                var trig = Child(root, "Portal", new Vector2(0f, 0.5f));
                var t = trig.AddComponent<BoxCollider2D>();
                t.isTrigger = true;
                t.size = new Vector2(0.7f, 1.7f);
                var sr = AddSprite(root, Art.Portal(variant, 0), new Vector2(0f, 0.5f), 4);
                SpriteAnim.Add(sr.gameObject, 4f, Art.Portal(variant, 0), Art.Portal(variant, 1));
                var portal = trig.AddComponent<Portal>();
                portal.Item = item;
                portal.Sprite = sr;
                break;
            }

            case ItemType.Esteira:
            {
                var sr = AddSprite(root, Art.Conveyor(0), new Vector2(0f, 0.25f), 5);
                SpriteAnim.Add(sr.gameObject, 8f, Art.Conveyor(0), Art.Conveyor(1));
                AddBox(root, new Vector2(0f, 0.25f), new Vector2(3f, 0.5f));
                root.AddComponent<Surface>().ConveyorSpeed = 4f;
                break;
            }

            case ItemType.LancaChamas:
            {
                AddSprite(root, Art.Flamethrower, Vector2.zero, 6);
                AddBox(root, Vector2.zero, Vector2.one);
                var flame = Child(root, "Fogo", new Vector2(0.5f, 0f));
                var fsr = flame.AddComponent<SpriteRenderer>();
                fsr.sortingOrder = 7;
                SpriteAnim.Add(flame, 14f, Art.Flame(0), Art.Flame(1));
                var t = flame.AddComponent<BoxCollider2D>();
                t.isTrigger = true;
                t.size = new Vector2(2.4f, 0.55f);
                t.offset = new Vector2(1.25f, 0f);
                flame.AddComponent<Hazard>();
                var ft = root.AddComponent<Flamethrower>();
                ft.Item = item;
                ft.Flame = flame;
                AddPreview(root, new Vector2(1.75f, 0f), new Vector2(2.5f, 0.6f), new Color(1f, 0.5f, 0.2f, 0.3f));
                break;
            }

            case ItemType.CaixaPodre:
            {
                var sr = AddSprite(root, Art.RottenCrate, Vector2.zero, 5);
                var body = AddBox(root, Vector2.zero, Vector2.one);
                var cr = root.AddComponent<Crumble>();
                cr.Item = item;
                cr.Sprite = sr;
                cr.Body = body;
                break;
            }

            case ItemType.PlataformaMovel:
            {
                var rail = AddSprite(root, Art.SawRail(MovingBlock.Travel + 1), Vector2.zero, 3);
                var plat = Child(root, "Plataforma", Vector2.zero);
                var rb = plat.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                AddBox(plat, new Vector2(0f, 0.25f), new Vector2(3f, 0.5f));
                AddSprite(plat, Art.MovingPlatform, new Vector2(0f, 0.25f), 5);
                var mover = root.AddComponent<MovingBlock>();
                mover.Body = rb;
                mover.Rail = rail.transform;
                mover.SetDirection(0);
                break;
            }

            case ItemType.Gangorra:
            {
                AddSprite(root, Art.SeesawBase, new Vector2(0f, -1f), 5);
                AddBox(root, new Vector2(0f, -1f), new Vector2(0.6f, 1f)); // apoio
                var plank = Child(root, "Prancha", new Vector2(0f, -0.3f));
                var rb = plank.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                AddBox(plank, Vector2.zero, new Vector2(5f, 0.44f));
                AddSprite(plank, Art.SeesawPlank, Vector2.zero, 5);
                root.AddComponent<Seesaw>().Plank = rb;
                break;
            }

            case ItemType.Ventilador:
            {
                AddSprite(root, Art.FanBase, Vector2.zero, 6);
                var blades = AddSprite(root, Art.FanBlades, new Vector2(0f, -0.06f), 7);
                blades.gameObject.AddComponent<Spinner>().DegreesPerSecond = 900f;
                AddBox(root, Vector2.zero, Vector2.one);
                var wind = Child(root, "Vento", new Vector2(0f, 0.5f + Wind.Length / 2f));
                var t = wind.AddComponent<BoxCollider2D>();
                t.isTrigger = true;
                t.size = new Vector2(0.9f, Wind.Length);
                wind.AddComponent<Wind>().Item = item;
                AddPreview(root, wind.transform.localPosition, new Vector2(0.9f, Wind.Length), new Color(0.6f, 0.85f, 1f, 0.25f));
                break;
            }
        }

        item.CacheParts();
        return item;
    }

    static GameObject Child(GameObject parent, string name, Vector2 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        return go;
    }

    static SpriteRenderer AddSprite(GameObject parent, Sprite sprite, Vector2 localPos, int order)
    {
        var go = Child(parent, "Visual", localPos);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    /// <summary>Área de efeito mostrada só enquanto o jogador posiciona o item (filho "Raio").</summary>
    static void AddPreview(GameObject root, Vector2 localPos, Vector2 size, Color color)
    {
        var sr = AddSprite(root, Art.Pixel, localPos, 4);
        sr.gameObject.name = "Raio";
        sr.transform.localScale = new Vector3(size.x, size.y, 1f);
        sr.color = color;
        sr.gameObject.SetActive(false);
    }

    static BoxCollider2D AddBox(GameObject go, Vector2 offset, Vector2 size)
    {
        var b = go.AddComponent<BoxCollider2D>();
        b.offset = offset;
        b.size = size;
        return b;
    }
}
