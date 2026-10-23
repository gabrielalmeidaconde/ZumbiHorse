using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu "Zumbi Horse": cria a cena do jogo e faz builds.
/// Também abre a cena do jogo automaticamente na primeira vez que o projeto é aberto.
/// </summary>
[InitializeOnLoad]
public static class ZumbiSetup
{
    const string ScenePath = "Assets/Scenes/Jogo.unity";

    static ZumbiSetup()
    {
        EditorApplication.delayCall += OpenGameSceneOnce;
    }

    static void OpenGameSceneOnce()
    {
        if (Application.isBatchMode || SessionState.GetBool("zumbi_scene_opened", false)) return;
        SessionState.SetBool("zumbi_scene_opened", true);
        var active = EditorSceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(active.path) && !active.isDirty && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("Zumbi Horse/Recriar cena do jogo", priority = 0)]
    public static void CreateScene()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 9.4f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(16, 12, 34, 255);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        camGo.AddComponent<AudioListener>();

        new GameObject("Jogo").AddComponent<GameManager>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.companyName = "Gabriel Almeida Conde";
        PlayerSettings.productName = "Ultimate Zumbi Horse";
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        AssetDatabase.SaveAssets();
        Debug.Log("[ZumbiSetup] Cena criada em " + ScenePath);
    }

    [MenuItem("Zumbi Horse/Build Linux", priority = 20)]
    public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/ZumbiHorse.x86_64");

    [MenuItem("Zumbi Horse/Build Windows", priority = 21)]
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/ZumbiHorse.exe");

    /// <summary>App universal (Mac Intel e Apple Silicon).</summary>
    [MenuItem("Zumbi Horse/Build Mac", priority = 22)]
    public static void BuildMac()
    {
        EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture", "x64ARM64");
        Build(BuildTarget.StandaloneOSX, "Builds/Mac/ZumbiHorse.app");
    }

    /// <summary>Versão para navegador (para jogar direto na página do itch.io).</summary>
    [MenuItem("Zumbi Horse/Build WebGL (itch.io)", priority = 23)]
    public static void BuildWebGL()
    {
        // o itch.io não manda o cabeçalho de compressão: com o fallback o próprio jogo descompacta no navegador
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        Build(BuildTarget.WebGL, "Builds/WebGL");
    }

    static void Build(BuildTarget target, string path)
    {
        // permite gerar em outra pasta: -zumbiBuildPath caminho/arquivo
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-zumbiBuildPath");
        if (i >= 0 && i + 1 < args.Length) path = args[i + 1];

        if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
        {
            string msg = $"O módulo de build para {target} não está instalado nesta versão do Unity.\n" +
                         "Instale pelo Unity Hub: Installs > (esta versão) > engrenagem > Add modules.";
            Debug.LogError("[ZumbiSetup] " + msg);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else EditorUtility.DisplayDialog("Zumbi Horse", msg, "OK");
            return;
        }

        if (!File.Exists(ScenePath)) CreateScene();
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = target,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        bool ok = report.summary.result == BuildResult.Succeeded;
        Debug.Log($"[ZumbiSetup] Build {target}: {report.summary.result} → {path}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>Para linha de comando: -executeMethod ZumbiSetup.BatchSetup</summary>
    public static void BatchSetup()
    {
        CreateScene();
        EditorApplication.Exit(0);
    }
}
