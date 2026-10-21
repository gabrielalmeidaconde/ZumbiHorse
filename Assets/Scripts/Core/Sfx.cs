using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Efeitos sonoros sintetizados na hora (nenhum arquivo de áudio).</summary>
public static class Sfx
{
    const int Rate = 44100;
    static AudioSource source;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static readonly System.Random rng = new System.Random(1234);

    public static void Init(GameObject host)
    {
        source = host.GetComponent<AudioSource>();
        if (source == null) source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        clips.Clear();

        Add("jump", 0.14f, t => Square(Lerp(320, 640, t / 0.14f), t) * Env(t, 0.14f) * 0.35f);
        Add("land", 0.07f, t => Noise() * Env(t, 0.07f) * 0.25f);
        Add("pick", 0.16f, t => Square(t < 0.07f ? 660 : 990, t) * Env(t, 0.16f) * 0.3f);
        Add("place", 0.2f, t => (Sine(Lerp(150, 50, t / 0.2f), t) * 0.8f + Noise() * 0.25f * Env(t, 0.04f)) * Env(t, 0.2f) * 0.7f);
        Add("rotate", 0.05f, t => Square(1200, t) * Env(t, 0.05f) * 0.15f);
        Add("error", 0.16f, t => Square(110, t) * Env(t, 0.16f) * 0.3f);
        Add("spring", 0.35f, t => Sine(300 + 260 * t / 0.35f + 90 * Mathf.Sin(t * 70), t) * Env(t, 0.35f) * 0.5f);
        Add("shoot", 0.14f, t => (Square(Lerp(900, 250, t / 0.14f), t) * 0.4f + Noise() * 0.3f) * Env(t, 0.14f) * 0.4f);
        Add("death", 0.45f, t => (Saw(Lerp(220, 55, t / 0.45f), t) * 0.6f + Noise() * 0.3f) * Env(t, 0.45f) * 0.45f);
        Add("bomb", 0.8f, t => LowNoise(0.18f) * Env(t, 0.8f) * Env(t, 0.8f) * 0.9f + Sine(Lerp(70, 30, t / 0.8f), t) * Env(t, 0.5f) * 0.5f);
        Add("goal", 0.42f, t => Square(Arp(t, 0.09f, 523, 659, 784, 1047), t) * Env(t, 0.42f) * 0.25f);
        Add("tick", 0.04f, t => Square(1000, t) * Env(t, 0.04f) * 0.2f);
        Add("go", 0.3f, t => Square(t < 0.12f ? 784 : 1047, t) * Env(t, 0.3f) * 0.3f);
        Add("win", 1.2f, t => (Square(Arp(t, 0.12f, 523, 659, 784, 1047, 784, 1047, 1319, 1568), t) * 0.6f +
                               Sine(Arp(t, 0.24f, 262, 330, 392, 523), t) * 0.4f) * Env(t, 1.2f) * 0.3f);
        Add("groan", 0.75f, t =>
        {
            float f = 92 + 14 * Mathf.Sin(t * 6f) - 25 * t;
            return (Saw(f, t) * 0.6f + Saw(f * 1.5f, t) * 0.25f) * Mathf.Clamp01(t * 8f) * Env(t, 0.75f) * 0.35f;
        });
        Add("portal", 0.35f, t => Sine(300 + 900 * t / 0.35f + 60 * Mathf.Sin(t * 90), t) * Env(t, 0.35f) * 0.4f);
        Add("crack", 0.18f, t => (Noise() * 0.7f + Square(90, t) * 0.3f) * Env(t, 0.18f) * Env(t, 0.18f) * 0.6f);
        Add("flame", 0.6f, t => LowNoise(0.3f) * Mathf.Clamp01(t * 15f) * Env(t, 0.6f) * 0.6f);
        Add("horn", 0.5f, t => (Square(185, t) * 0.5f + Square(233, t) * 0.5f) * Mathf.Clamp01(t * 20f) * Env(t, 0.5f) * 0.25f);
        Add("join", 0.3f, t => Square(Arp(t, 0.07f, 392, 523, 659, 784), t) * Env(t, 0.3f) * 0.25f);
    }

    public static void Play(string name, float volume = 1f, float pitch = 1f)
    {
        if (source == null || !clips.TryGetValue(name, out var clip)) return;
        source.pitch = pitch;
        source.PlayOneShot(clip, volume);
    }

    /// <summary>Toca com uma leve variação de tom.</summary>
    public static void PlayVar(string name, float volume = 1f) =>
        Play(name, volume, 0.9f + (float)rng.NextDouble() * 0.2f);

    // ------------------------------------------------------------------ síntese

    static void Add(string name, float duration, Func<float, float> f)
    {
        int n = Mathf.CeilToInt(duration * Rate);
        var data = new float[n];
        lowState = 0f;
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        clips[name] = clip;
    }

    static float lowState;

    static float Lerp(float a, float b, float t) => a + (b - a) * Mathf.Clamp01(t);
    static float Env(float t, float d) => Mathf.Clamp01(1f - t / d);
    static float Sine(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);
    static float Square(float f, float t) => Mathf.Repeat(f * t, 1f) < 0.5f ? 1f : -1f;
    static float Saw(float f, float t) => Mathf.Repeat(f * t, 1f) * 2f - 1f;
    static float Noise() => (float)rng.NextDouble() * 2f - 1f;

    static float LowNoise(float k)
    {
        lowState += (Noise() - lowState) * k;
        return lowState * 2.5f;
    }

    static float Arp(float t, float step, params float[] notes)
    {
        int i = Mathf.Min((int)(t / step), notes.Length - 1);
        return notes[i];
    }
}
