using System;
using System.Collections.Generic;

/// <summary>
/// Sintetizador das músicas (chiptune, como os efeitos do Sfx): cada música é escrita em texto
/// (acordes, melodia e padrões de baixo, arpejo e bateria) e vira um loop de amostras.
/// Não usa nada da Unity, então dá para testar fora dela.
/// </summary>
public static class MusicSynth
{
    public const int Rate = 44100;
    /// <summary>Passos (semicolcheias) por compasso.</summary>
    public const int BarSteps = 16;

    public enum Lead { Theremin, Pulse, Pluck }

    public class Song
    {
        public string Name;
        public float Bpm;
        public int Bars;
        /// <summary>Acordes divididos igualmente pela música (ex.: "Am F C G"); "m" é menor e "7" acrescenta a sétima.</summary>
        public string Chords;
        /// <summary>Melodia: "nota:passos" separados por espaço ("e5:2 a5:2 .:4"); "." é pausa e "bb4" é si bemol.</summary>
        public string Melody;
        public Lead LeadSound;
        /// <summary>Baixo, um caractere por passo (o padrão se repete): 1 fundamental, 3 terça, 5 quinta, 7 sétima,
        /// 8 oitava, "-" segura a nota anterior, "." pausa.</summary>
        public string Bass;
        /// <summary>Arpejo, um caractere por passo: 0 a 5 = notas do acorde de baixo para cima, "-" segura, "." pausa.</summary>
        public string Arp;
        /// <summary>Bateria, um caractere por passo: "x" toca, "o" chimbal aberto, "." nada.</summary>
        public string Kick, Snare, Hat;
    }

    enum Voice { Lead, Arp, Bass, Kick, Snare, Hat, OpenHat }

    struct Note
    {
        public Voice Voice;
        public int Start, Length;   // em amostras
        public float Freq, PrevFreq;
    }

    /// <summary>
    /// Uma música sendo sintetizada. Run sintetiza um pedaço de cada vez (para não travar um quadro inteiro);
    /// quando termina, Data tem o loop pronto (o fim emenda no começo).
    /// </summary>
    public sealed class Job
    {
        public readonly float[] Data;
        readonly List<Note> notes = new List<Note>();
        readonly Lead lead;
        readonly Random rng = new Random(77);
        int next;
        bool done;

        /// <param name="tempo">Multiplica o andamento sem mudar o tom (1,2 = 20% mais rápida).</param>
        public Job(Song s, float tempo = 1f)
        {
            lead = s.LeadSound;
            int steps = s.Bars * BarSteps;
            double step = Rate * 60.0 / (s.Bpm * tempo) / 4.0;
            Data = new float[(int)Math.Round(steps * step)];
            Build(s, steps, step);
        }

        public bool Done => done;

        /// <summary>Sintetiza notas até gastar "budget" amostras. Retorna true quando a música ficou pronta.</summary>
        public bool Run(int budget = int.MaxValue)
        {
            while (!done && budget > 0)
            {
                if (next < notes.Count) budget -= Render(notes[next++]);
                else { Normalize(); done = true; }
            }
            return done;
        }

        // ------------------------------------------------------------ partitura

        void Build(Song s, int steps, double step)
        {
            int At(int st) => (int)Math.Round(st * step);
            void Add(Voice v, int st, int len, float freq, float prev = 0f) =>
                notes.Add(new Note { Voice = v, Start = At(st), Length = At(st + len) - At(st), Freq = freq, PrevFreq = prev > 0f ? prev : freq });

            var chords = Split(s.Chords);
            int[] ChordAt(int st) => ParseChord(chords[Math.Min(chords.Length - 1, st * chords.Length / steps)]);

            // melodia
            var mel = new List<(int st, int len, int midi)>();
            int pos = 0;
            foreach (var tok in Split(s.Melody))
            {
                int c = tok.IndexOf(':');
                int len = c < 0 ? 2 : int.Parse(tok.Substring(c + 1));
                string name = c < 0 ? tok : tok.Substring(0, c);
                if (name != ".") mel.Add((pos, len, ParseNote(name)));
                pos += len;
            }
            if (pos != steps) throw new ArgumentException($"{s.Name}: a melodia tem {pos} passos, mas a música tem {steps}");
            for (int i = 0; i < mel.Count; i++)
            {
                var prev = mel[(i + mel.Count - 1) % mel.Count];
                Add(Voice.Lead, mel[i].st, mel[i].len, Freq(mel[i].midi), Freq(prev.midi));
            }

            for (int st = 0; st < steps; st++)
            {
                int[] chord = ChordAt(st);

                char b = Pattern(s.Bass, st);
                if (b >= '1' && b <= '8')
                {
                    int iv = b == '3' ? chord[2] : b == '5' ? 7 : b == '7' ? 10 : b == '8' ? 12 : 0;
                    Add(Voice.Bass, st, Held(s.Bass, st, steps), Freq(36 + chord[0] + iv));
                }

                char a = Pattern(s.Arp, st);
                if (a >= '0' && a <= '5')
                {
                    int k = chord.Length - 1, d = a - '0';
                    Add(Voice.Arp, st, Held(s.Arp, st, steps), Freq(60 + chord[0] + chord[1 + d % k] + 12 * (d / k)));
                }

                if (Pattern(s.Kick, st) == 'x') Add(Voice.Kick, st, 1, 0f);
                if (Pattern(s.Snare, st) == 'x') Add(Voice.Snare, st, 1, 0f);
                char h = Pattern(s.Hat, st);
                if (h == 'x') Add(Voice.Hat, st, 1, 0f);
                else if (h == 'o') Add(Voice.OpenHat, st, 1, 0f);
            }
        }

        static char Pattern(string p, int st) => string.IsNullOrEmpty(p) ? '.' : p[st % p.Length];

        /// <summary>Quantos passos a nota dura: ela mesma mais os "-" que vêm depois.</summary>
        static int Held(string p, int st, int steps)
        {
            int len = 1;
            while (len < steps && Pattern(p, st + len) == '-') len++;
            return len;
        }

        // ------------------------------------------------------------ síntese

        /// <summary>Soma a nota no loop e retorna quantas amostras ela ocupou.</summary>
        int Render(Note n)
        {
            float dur = n.Length / (float)Rate;
            float tail = n.Voice switch
            {
                Voice.Lead => lead == Lead.Pluck ? Math.Min(dur, 0.5f) + 0.02f : dur + 0.08f,
                Voice.Arp => Math.Min(dur + 0.02f, 0.3f),
                Voice.Bass => dur + 0.015f,
                Voice.Kick => 0.3f,
                Voice.Snare => 0.25f,
                Voice.Hat => 0.08f,
                _ => 0.3f,
            };
            int len = (int)(tail * Rate);
            double phase = 0;
            float low = 0f;
            for (int j = 0; j < len; j++)
            {
                float t = j / (float)Rate, v;
                switch (n.Voice)
                {
                    case Voice.Lead:
                        v = LeadSample(n, t, dur, ref phase);
                        break;
                    case Voice.Arp:
                        phase += n.Freq / Rate;
                        v = Pulse(phase, 0.125) * Exp(t, 0.07f) * Release(t, dur + 0.02f, 0.01f) * 0.055f;
                        break;
                    case Voice.Bass:
                        phase += n.Freq / Rate;
                        v = Triangle(phase) * Attack(t, 0.002f) * Release(t, dur, 0.015f) * 0.24f;
                        break;
                    case Voice.Kick:
                        phase += (50f + 110f * Exp(t, 0.03f)) / Rate;
                        v = (float)Math.Sin(phase * 2 * Math.PI) * Exp(t, 0.12f) * 0.45f;
                        break;
                    case Voice.Snare:
                        v = (Noise() * Exp(t, 0.09f) * 0.7f + (float)Math.Sin(t * 2 * Math.PI * 180) * Exp(t, 0.04f) * 0.5f) * 0.22f;
                        break;
                    default:
                        // chimbal: ruído sem os graves
                        float noise = Noise();
                        low += (noise - low) * 0.3f;
                        v = (noise - low) * Exp(t, n.Voice == Voice.Hat ? 0.018f : 0.09f) * 0.07f;
                        break;
                }
                Data[(n.Start + j) % Data.Length] += v;
            }
            return len;
        }

        float LeadSample(Note n, float t, float dur, ref double phase)
        {
            switch (lead)
            {
                case Lead.Theremin:
                {
                    // desliza da nota anterior e vibra depois de um instante, como um teremim
                    float f = n.PrevFreq * (float)Math.Pow(n.Freq / n.PrevFreq, Math.Min(1f, t / 0.06f));
                    f *= 1f + 0.012f * Math.Min(1f, Math.Max(0f, t - 0.1f) / 0.2f) * (float)Math.Sin(t * 2 * Math.PI * 5f);
                    phase += f / Rate;
                    double p = phase * 2 * Math.PI;
                    return (float)(Math.Sin(p) + 0.25 * Math.Sin(2 * p)) * Attack(t, 0.03f) * Release(t, dur, 0.08f) * 0.2f;
                }
                case Lead.Pluck:
                    phase += n.Freq / Rate;
                    return Pulse(phase, 0.5) * Exp(t, 0.16f) * Release(t, Math.Min(dur, 0.5f), 0.02f) * 0.13f;
                default:
                {
                    float f = n.Freq * (1f + (t > 0.18f ? 0.006f * (float)Math.Sin(t * 2 * Math.PI * 5.5f) : 0f));
                    phase += f / Rate;
                    float env = t < 0.1f ? 1f - 0.3f * t / 0.1f : 0.7f;
                    return Pulse(phase, 0.25) * env * Attack(t, 0.004f) * Release(t, dur, 0.03f) * 0.13f;
                }
            }
        }

        /// <summary>Deixa todas as músicas com o mesmo volume médio (sem passar do pico).</summary>
        void Normalize()
        {
            float peak = 0f;
            double sum = 0;
            foreach (var v in Data) { peak = Math.Max(peak, Math.Abs(v)); sum += v * v; }
            if (peak <= 0f) return;
            float k = Math.Min(0.95f / peak, 0.16f / (float)Math.Sqrt(sum / Data.Length));
            for (int i = 0; i < Data.Length; i++) Data[i] *= k;
        }

        float Noise() => (float)rng.NextDouble() * 2f - 1f;
    }

    // ---------------------------------------------------------------- formas de onda e envelopes

    static float Pulse(double phase, double duty) => phase - Math.Floor(phase) < duty ? 1f : -1f;
    static float Triangle(double phase) { double x = phase - Math.Floor(phase); return (float)(x < 0.5 ? 4 * x - 1 : 3 - 4 * x); }
    static float Exp(float t, float tau) => (float)Math.Exp(-t / tau);
    static float Attack(float t, float a) => Math.Min(1f, t / a);
    /// <summary>1 até "end" e cai até 0 em "r" segundos.</summary>
    static float Release(float t, float end, float r) => t <= end ? 1f : Math.Max(0f, 1f - (t - end) / r);

    // ---------------------------------------------------------------- notas e acordes

    static string[] Split(string s) => (s ?? "").Split(new[] { ' ', '\n', '\r', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries);

    static float Freq(int midi) => 440f * (float)Math.Pow(2, (midi - 69) / 12.0);

    static readonly int[] Letters = { 9, 11, 0, 2, 4, 5, 7 }; // a b c d e f g

    /// <summary>Altura (0 a 11) de "c", "f#", "bb"... e quantos caracteres ela usou.</summary>
    static int PitchClass(string s, out int used)
    {
        int pc = Letters[char.ToLowerInvariant(s[0]) - 'a'];
        used = 1;
        if (s.Length > 1 && s[1] == '#') { pc++; used = 2; }
        else if (s.Length > 1 && s[1] == 'b') { pc--; used = 2; }
        return (pc + 12) % 12;
    }

    /// <summary>"a4" = 69 (lá 440 Hz), "c#5", "bb3"...</summary>
    static int ParseNote(string s)
    {
        int pc = PitchClass(s, out int used);
        return 12 * (int.Parse(s.Substring(used)) + 1) + pc;
    }

    /// <summary>[altura da fundamental (0 a 11), depois as notas do acorde em semitons: 0, terça, quinta, (sétima)].</summary>
    static int[] ParseChord(string s)
    {
        int root = PitchClass(s, out int used);
        string rest = s.Substring(used);
        int third = rest.StartsWith("m") ? 3 : 4;
        return rest.Contains("7") ? new[] { root, 0, third, 7, 10 } : new[] { root, 0, third, 7 };
    }

    // ================================================================ as músicas

    /// <summary>Lobby e menus: teremim de filme de terror B, em lá menor.</summary>
    public static readonly Song Lobby = new Song
    {
        Name = "Lobby",
        Bpm = 112,
        Bars = 8,
        LeadSound = Lead.Theremin,
        Chords = "Am Am F F C C G G | Am Am F F Dm E7 E7 E7",
        Melody = "e5:2 a5:2 c6:2 b5:2 a5:4 e5:4 | f5:2 a5:2 c6:2 a5:2 f5:6 .:2 | g5:2 e5:2 c5:2 e5:2 g5:4 c6:4 | b5:3 a5:1 g5:2 d5:2 g5:6 .:2 |" +
                 "e5:2 a5:2 c6:2 e6:2 d6:2 c6:2 b5:2 a5:2 | c6:4 a5:2 f5:2 a5:4 c6:4 | d6:2 c6:2 a5:2 f5:2 e5:2 g#5:2 b5:2 d6:2 | e6:6 d6:2 b5:4 g#5:4",
        Bass = "1-..8-..5-..8-..",
        Arp = "..0...2...1...2.",
        Kick = "x.......x.x.....",
        Snare = "....x.......x...",
        Hat = "..x...x...x...x.",
    };

    /// <summary>Caixa de itens, posicionamento, placar e criador de fases: pizzicato sorrateiro em ré menor.</summary>
    public static readonly Song Box = new Song
    {
        Name = "Caixa",
        Bpm = 96,
        Bars = 8,
        LeadSound = Lead.Pluck,
        Chords = "Dm Dm Bb Bb Gm Gm A7 A7 | Dm Dm Bb Bb Gm A7 Dm Dm",
        Melody = "d4:2 .:2 f4:2 .:2 a4:3 g4:1 f4:2 e4:2 | f4:2 .:2 bb4:2 .:2 d5:4 c5:2 bb4:2 | g4:2 bb4:2 d5:2 bb4:2 g4:4 .:4 | a4:2 c#5:2 e5:2 c#5:2 a4:2 g4:2 f4:2 e4:2 |" +
                 "d4:2 .:2 f4:2 .:2 a4:3 g4:1 f4:2 a4:2 | d5:4 .:2 c5:2 bb4:2 a4:2 g4:2 f4:2 | g4:2 bb4:2 d5:4 e5:2 c#5:2 a4:4 | d5:6 a4:2 f4:2 e4:2 d4:4",
        Bass = "1-.1-.5.8-.8-.5.",
        Arp = "0..2..1.3..2..1.",
        Kick = "x.......x.......",
        Snare = "....x.......x...",
        Hat = "..x...x...x...x.",
    };

    /// <summary>Corrida (e o desafio solo): rápida, em mi menor, com uma parte A e uma parte B.</summary>
    public static readonly Song Race = new Song
    {
        Name = "Corrida",
        Bpm = 150,
        Bars = 16,
        LeadSound = Lead.Pulse,
        Chords = "Em Em C C D D B B | Em Em C C Am B Em Em | C C D D Em Em Em Em | C C D D B B B B",
        Melody = "e5:2 b4:2 e5:2 g5:2 f#5:2 e5:2 d5:2 e5:2 | c5:2 e5:2 g5:4 e5:2 c5:2 e5:4 | d5:2 f#5:2 a5:2 f#5:2 d5:2 a4:2 d5:4 | d#5:4 f#5:4 b5:6 .:2 |" +
                 "e5:2 b4:2 e5:2 g5:2 b5:2 a5:2 g5:2 f#5:2 | g5:4 e5:2 c5:2 e5:2 g5:2 c6:4 | a5:2 g5:2 e5:2 c5:2 b4:2 d#5:2 f#5:2 a5:2 | g5:4 f#5:2 d#5:2 e5:8 |" +
                 "g5:3 g5:1 e5:2 g5:2 c6:4 b5:2 g5:2 | a5:3 a5:1 f#5:2 a5:2 d6:4 c6:2 a5:2 | b5:4 g5:2 e5:2 b5:4 g5:2 e5:2 | f#5:2 g5:2 a5:2 g5:2 f#5:2 e5:2 d5:2 b4:2 |" +
                 "c5:2 e5:2 g5:2 c6:2 b5:2 g5:2 e5:2 g5:2 | d5:2 f#5:2 a5:2 d6:2 c6:2 a5:2 f#5:2 a5:2 | b5:4 a5:2 f#5:2 d#5:4 f#5:4 | b4:2 d#5:2 f#5:2 b5:2 a5:2 f#5:2 d#5:2 b4:2",
        Bass = "1.8.1.8.1.8.5.8.",
        Arp = "0123012301230123",
        Kick = "x...x...x...x...",
        Snare = "....x.......x.......x.......x.xx",
        Hat = "x.x.x.x.x.x.x.x.",
    };

    /// <summary>Andamento da corrida quando aparece "SÓ FALTAM OS BOTS!".</summary>
    public const float HurryTempo = 1.2f;

    /// <summary>Vitória: fanfarra em dó maior.</summary>
    public static readonly Song Victory = new Song
    {
        Name = "Vitória",
        Bpm = 132,
        Bars = 8,
        LeadSound = Lead.Pulse,
        Chords = "C F G C Am F G7 C",
        Melody = "c5:2 e5:2 g5:2 c6:6 g5:2 c6:2 | a5:4 f5:2 a5:2 c6:6 a5:2 | b5:4 g5:2 d5:2 g5:2 b5:2 d6:4 | c6:6 b5:2 c6:2 g5:2 e5:4 |" +
                 "e5:2 a5:2 c6:2 a5:2 e6:4 c6:4 | f5:2 a5:2 c6:2 f6:2 e6:2 c6:2 a5:4 | d6:2 b5:2 g5:2 b5:2 d6:2 f6:2 e6:2 d6:2 | c6:8 g5:4 .:4",
        Bass = "1-5-8-5-1-5-8-5-",
        Arp = "0.1.2.1.0.1.2.1.",
        Kick = "x...x...x...x...",
        Snare = "....x.......x...",
        Hat = "..x...x...x...x.",
    };
}
