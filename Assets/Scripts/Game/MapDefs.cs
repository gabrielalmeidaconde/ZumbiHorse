using UnityEngine;

/// <summary>Um trem fantasma que vai e volta sobre um trilho.</summary>
public class TrainDef
{
    public int TrackRow;      // linha do trilho ('=')
    public int FromX, ToX;    // colunas (inclusive) por onde o trem pode andar
    public int Wagons = 1;
    public float Speed = 4f;
    public float StartT;      // posição inicial (0 = esquerda, 1 = direita)
}

/// <summary>Serra que vai e volta entre duas células.</summary>
public class SawPathDef
{
    public Vector2Int From, To;
    public float Speed = 2.5f;
}

/// <summary>
/// Mapa desenhado em texto: 18 linhas (de cima para baixo) x 32 colunas.
///   .  vazio          #  terra (a bomba destrói)   S  pedra (indestrutível)
///   i  gelo           =  trilho do trem             a  ácido (mata)
///   ^  espinhos       X  serra fixa                  1-4  onde cada jogador nasce
///   G  cérebro        p  pedestal do cérebro         t  lápide (enfeite)   T  árvore (enfeite)
/// </summary>
public class MapDef
{
    public string Name;
    public string Description;
    public int Theme;
    public string[] Rows;
    public TrainDef[] Trains = new TrainDef[0];
    public SawPathDef[] Saws = new SawPathDef[0];

    /// <summary>Caractere na célula (x, y), com y = 0 na linha de baixo.</summary>
    public char At(int x, int y)
    {
        int row = Rows.Length - 1 - y;
        if (row < 0 || row >= Rows.Length || x < 0 || x >= Rows[row].Length) return '.';
        return Rows[row][x];
    }
}

public static class Maps
{
    public static readonly MapDef[] All =
    {
        new MapDef
        {
            Name = "Cemitério",
            Description = "O clássico: três buracos e um paredão.",
            Theme = 0,
            Rows = new[]
            {
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                ".............................G..",
                ".............................p.t",
                ".1234T.....................SSSSS",
                "SSSSSS................##...SSSSS",
                "SSSSSS.....t.t......t.##...SSSSS",
                "SSSSSS....#####....#####...SSSSS",
                "SSSSSS....#####....#####...SSSSS",
                "SSSSSS....#####....#####...SSSSS",
            },
        },
        new MapDef
        {
            Name = "Estação Fantasma",
            Description = "Um trem fantasma vai e volta no fundo do vale.",
            Theme = 1,
            Rows = new[]
            {
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                ".............######.............",
                ".............................G..",
                "T1234...........X............p..",
                "SSSSS......................SSSSS",
                "SSSSSt....................tSSSSS",
                "SSSSS##..................##SSSSS",
                "SSSSS##..................##SSSSS",
                "SSSSS##==================##SSSSS",
                "SSSSS######################SSSSS",
            },
            Trains = new[] { new TrainDef { TrackRow = 1, FromX = 7, ToX = 24, Wagons = 1, Speed = 4.5f } },
        },
        new MapDef
        {
            Name = "Serraria",
            Description = "Serras fixas, serras móveis e um poço de espinhos.",
            Theme = 2,
            Rows = new[]
            {
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................X...............",
                "..............................G.",
                "1234..........................p.",
                "SSSS....X.t...####..........SSSS",
                "SSSS..#####...####..#..#####SSSS",
                "SSSS..#####^^^####..#..#####SSSS",
                "SSSS..############..#..#####SSSS",
            },
            Saws = new[]
            {
                new SawPathDef { From = new Vector2Int(20, 5), To = new Vector2Int(20, 9), Speed = 2.5f },
                new SawPathDef { From = new Vector2Int(23, 3), To = new Vector2Int(27, 3), Speed = 3f },
            },
        },
        new MapDef
        {
            Name = "Pântano Ácido",
            Description = "Ilhas no meio do ácido, chão de gelo e uma serra.",
            Theme = 3,
            Rows = new[]
            {
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "...............###..............",
                "................................",
                "........###.....................",
                "................................",
                "................................",
                ".............t................G.",
                "1234........##.........##.....p.",
                "SSSS...t....##.........##...SSSS",
                "SSSSaa###aaa##aaaiiiaaa##aaaSSSS",
                "SSSSaa###aaa##aaa###aaa##aaaSSSS",
            },
            Saws = new[] { new SawPathDef { From = new Vector2Int(21, 2), To = new Vector2Int(21, 6), Speed = 2f } },
        },
        new MapDef
        {
            Name = "Ferrovia Dupla",
            Description = "Dois trens em pontes, uma delas quebrada, sobre o ácido.",
            Theme = 4,
            Rows = new[]
            {
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "..............................G.",
                "1234..........................p.",
                "SSSS==========....==========SSSS",
                "SSSS........................SSSS",
                "SSSS........................SSSS",
                "SSSS........................SSSS",
                "SSSS========================SSSS",
                "SSSS........................SSSS",
                "SSSSaaaaaaaaaaaaaaaaaaaaaaaaSSSS",
            },
            Trains = new[]
            {
                new TrainDef { TrackRow = 6, FromX = 6, ToX = 25, Wagons = 1, Speed = 5f, StartT = 0.3f },
                new TrainDef { TrackRow = 2, FromX = 5, ToX = 26, Wagons = 2, Speed = 3.5f, StartT = 1f },
            },
        },
    };
}
