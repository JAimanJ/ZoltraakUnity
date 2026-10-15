using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Entry point. Put this on ONE empty GameObject in the scene and press Play.
/// Sets up lighting / sky / fog / post-processing, then builds the arena, player and boss in code.
/// Everything spawned lives under "World" so R can wipe and rebuild it instantly.
public class Bootstrap : MonoBehaviour
{
    public static Bootstrap Instance { get; private set; }
    public static Transform World { get; private set; }

    static readonly Vector3 PlayerSpawn = new Vector3(0f, 3f, 12f);
    static readonly Vector3 BossSpawn = new Vector3(0f, 10f, -25f);

    void Start()
    {
        Instance = this;
        Application.targetFrameRate = 144;
        SetupEnvironment();
        Build();
    }

    void Build()
    {
        Time.timeScale = 1f;
        World = new GameObject("World").transform;
        Arena.Build(World);
        PlayerController.Create(PlayerSpawn, 180f);   // facing -Z, toward the boss
        Boss.Create(BossSpawn);
    }

    public void Restart() => StartCoroutine(RestartRoutine());

    IEnumerator RestartRoutine()
    {
        Time.timeScale = 1f;
        if (World != null) Destroy(World.gameObject);
        yield return null;   // let Unity finish destroying
        Build();
    }

    void SetupEnvironment()
    {
        // The template's camera is replaced by the player's camera
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            c.gameObject.SetActive(false);

        // Sun: late afternoon, warm, soft shadows
        Light sun = null;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(38f, -40f, 0f);
        sun.color = new Color(1f, 0.94f, 0.84f);
        sun.intensity = 1.3f;
        sun.shadows = LightShadows.Soft;

        // Sky
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_SunSize", 0.04f);
        sky.SetFloat("_AtmosphereThickness", 0.9f);
        sky.SetColor("_SkyTint", new Color(0.5f, 0.6f, 0.85f));
        sky.SetColor("_GroundColor", new Color(0.35f, 0.4f, 0.35f));
        sky.SetFloat("_Exposure", 1.2f);
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();

        // Fog for depth
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.005f;
        RenderSettings.fogColor = new Color(0.78f, 0.84f, 0.93f);

        // Post-processing: bloom (makes magic glow), ACES tonemapping, light vignette
        var vgo = new GameObject("ZoltraakPostFX");
        var vol = vgo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        vol.sharedProfile = profile;
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(1.2f);
        bloom.threshold.Override(1f);
        bloom.scatter.Override(0.65f);
        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);
        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.2f);
    }
}
