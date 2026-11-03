using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Música de fundo: escolhe a faixa pela fase da partida e troca com um crossfade.
/// Toca em dois AudioSources próprios (separados dos efeitos do Sfx), com volume próprio salvo em PlayerPrefs.
/// No online cada computador escolhe a faixa pela fase que recebe do host (a música não vai pela rede).
/// </summary>
public class Music : MonoBehaviour
{
    public enum Track { None, Lobby, Box, Race, RaceFast, Victory }

    const string PrefLevel = "config_musica";
    /// <summary>Volume da música em 100% (os efeitos ficam por cima).</summary>
    const float BaseVolume = 0.32f;
    const float PauseVolume = 0.35f, FadeTime = 0.8f, TempoFadeTime = 0.15f;
    /// <summary>A música de vitória espera o som "win" tocar.</summary>
    const float VictoryDelay = 1.1f;
    /// <summary>Amostras sintetizadas por quadro enquanto as outras faixas são preparadas.</summary>
    const int RenderBudget = 60000;

    static int level = -1;

    /// <summary>Volume da música de 0 (mudo) a 10.</summary>
    public static int Level
    {
        get
        {
            if (level < 0) level = Mathf.Clamp(PlayerPrefs.GetInt(PrefLevel, 10), 0, 10);
            return level;
        }
        set
        {
            level = Mathf.Clamp(value, 0, 10);
            PlayerPrefs.SetInt(PrefLevel, level);
        }
    }

    readonly AudioSource[] sources = new AudioSource[2];
    readonly Dictionary<Track, AudioClip> clips = new Dictionary<Track, AudioClip>();
    readonly Dictionary<Track, MusicSynth.Job> jobs = new Dictionary<Track, MusicSynth.Job>();
    int cur;                    // AudioSource da faixa atual (o outro está sumindo)
    Track playing = Track.None;
    float fade = 1f, fadeTime = FadeTime, duck = 1f;
#if UNITY_WEBGL && !UNITY_EDITOR
    bool unlocked;
#endif

    void Awake()
    {
        var go = new GameObject("Musica");
        go.transform.SetParent(transform, false);
        for (int i = 0; i < sources.Length; i++)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.priority = 0; // nunca é cortada quando tocam muitos efeitos juntos
            s.volume = 0f;
            sources[i] = s;
        }
    }

    IEnumerator Start()
    {
        Clip(Track.Lobby);
        // prepara as outras aos poucos, para estarem prontas quando a partida começar
        foreach (var t in new[] { Track.Box, Track.Race, Track.Victory, Track.RaceFast })
        {
            while (!clips.ContainsKey(t))
            {
                if (Prepare(t).Run(RenderBudget)) Finish(t);
                yield return null;
            }
        }
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        UnlockWebAudio();

        var want = Want(gm);
        if (want != playing) Switch(want);

        fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / fadeTime);
        duck = Mathf.MoveTowards(duck, gm.Paused ? PauseVolume : 1f, Time.unscaledDeltaTime * 2f);
        float vol = BaseVolume * Level / 10f * duck;
        var old = sources[1 - cur];
        sources[cur].volume = vol * fade;
        old.volume = vol * (1f - fade);
        if (fade >= 1f && old.isPlaying) old.Stop();
    }

    Track Want(GameManager gm)
    {
        switch (gm.Phase)
        {
            case Phase.Pick:
            case Phase.Place:
            case Phase.Score:
            case Phase.Editor:
                return Track.Box;
            case Phase.RoundEnd:
                // no último quadro da corrida o BotsOnly já desliga (não sobrou bot vivo): continua como estava
                return playing == Track.RaceFast ? Track.RaceFast : Track.Race;
            case Phase.Run:
            case Phase.Challenge:
                return gm.BotsOnly ? Track.RaceFast : Track.Race;
            case Phase.Victory:
                return Track.Victory;
            default:
                return Track.Lobby;
        }
    }

    void Switch(Track want)
    {
        var from = sources[cur];
        cur = 1 - cur;
        var to = sources[cur];
        to.Stop();
        // entre a corrida normal e a apressada a música continua do mesmo ponto
        bool tempo = (playing == Track.Race && want == Track.RaceFast) || (playing == Track.RaceFast && want == Track.Race);
        playing = want;
        fade = 0f;
        fadeTime = tempo ? TempoFadeTime : FadeTime;
        to.clip = want == Track.None ? null : Clip(want);
        if (to.clip == null) return;

        if (want == Track.Victory) { to.PlayDelayed(VictoryDelay); return; }
        to.Play();
        if (tempo && from.clip != null && from.isPlaying)
            to.timeSamples = (int)((long)from.timeSamples * to.clip.samples / from.clip.samples) % to.clip.samples;
    }

    /// <summary>
    /// O navegador só libera o som depois do primeiro clique ou tecla. Recomeça a faixa nessa hora,
    /// para ela tocar desde o começo mesmo que o navegador não tenha iniciado o que já estava "tocando".
    /// </summary>
    void UnlockWebAudio()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (unlocked) return;
        bool pressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                       (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                       (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame);
        if (!pressed) return;
        unlocked = true;
        var s = sources[cur];
        if (s.clip != null && playing != Track.Victory) { s.Stop(); s.Play(); }
#endif
    }

    // ------------------------------------------------------------------ faixas

    MusicSynth.Job Prepare(Track t)
    {
        if (!jobs.TryGetValue(t, out var job))
        {
            job = t switch
            {
                Track.Box => new MusicSynth.Job(MusicSynth.Box),
                Track.Race => new MusicSynth.Job(MusicSynth.Race),
                Track.RaceFast => new MusicSynth.Job(MusicSynth.Race, MusicSynth.HurryTempo),
                Track.Victory => new MusicSynth.Job(MusicSynth.Victory),
                _ => new MusicSynth.Job(MusicSynth.Lobby),
            };
            jobs[t] = job;
        }
        return job;
    }

    /// <summary>A faixa pronta (se ainda não terminou de ser preparada, termina agora).</summary>
    AudioClip Clip(Track t)
    {
        if (!clips.ContainsKey(t))
        {
            Prepare(t).Run();
            Finish(t);
        }
        return clips[t];
    }

    void Finish(Track t)
    {
        var data = jobs[t].Data;
        var clip = AudioClip.Create("musica_" + t, data.Length, 1, MusicSynth.Rate, false);
        clip.SetData(data, 0);
        clips[t] = clip;
        jobs.Remove(t);
    }
}
