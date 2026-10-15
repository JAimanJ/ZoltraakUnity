using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum Team { Player, Enemy }

/// Anything that can be hit by a projectile.
public interface IDamageable
{
    Team Team { get; }
    void TakeHit(int amount, Vector3 point, Vector3 direction);
}

/// Shared helpers: materials, primitives, lights, explosions, debris, smoke, GUI text.
public static class Fx
{
    static Shader litShader, addShader;
    static Material debrisMat;

    // ------------------------------------------------------------ Materials

    public static Material Lit(Color c, float smoothness = 0.15f)
    {
        if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(litShader);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    /// Opaque, lit, glowing (emission > 1 triggers bloom).
    public static Material Glow(Color c, float intensity)
    {
        var m = Lit(c, 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * intensity);
        return m;
    }

    /// Additive energy layer.
    public static Material Additive(Color c, float intensity)
    {
        if (addShader == null) addShader = Shader.Find("Zoltraak/Additive");
        var m = new Material(addShader);
        m.SetColor("_Color", c);
        m.SetFloat("_Intensity", intensity);
        return m;
    }

    // ------------------------------------------------------------ Objects

    /// Unity primitive with a material; optionally keeps its collider.
    public static GameObject Prim(PrimitiveType type, Material mat, bool collider, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent != null ? parent : Bootstrap.World, false);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        if (mat.shader == addShader)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return go;
    }

    public static Light PointLight(Transform parent, Vector3 worldPos, Color c, float intensity, float range)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent != null ? parent : Bootstrap.World, false);
        go.transform.position = worldPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }

    /// Simple cone mesh (radius 0.5, height 1, base at y = 0). Unity has no cone primitive.
    public static Mesh Cone(int segments)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments;
            float a1 = (i + 1) * Mathf.PI * 2f / segments;
            int b = verts.Count;
            verts.Add(new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f));
            verts.Add(new Vector3(0, 1, 0));
            verts.Add(new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f));
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
        }
        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ------------------------------------------------------------ Effects

    public static void Explosion(Vector3 pos, Vector3 normal, Color lightColor, float scale, bool debris)
    {
        var flashMat = Additive(Color.white, 3f);
        var flash = Prim(PrimitiveType.Sphere, flashMat, false);
        flash.transform.position = pos;
        FxRunner.Run(GrowFade(flash.transform, flashMat, 1f * scale, 4f * scale, 3f, 0.25f));

        var light = PointLight(null, pos + normal * 0.5f, lightColor, 8f * scale, 12f * scale);
        FxRunner.Run(FadeLight(light, 0.3f));

        if (!debris) return;
        if (debrisMat == null) debrisMat = Lit(new Color(0.45f, 0.43f, 0.4f));
        int count = Mathf.RoundToInt(10 * scale);
        for (int i = 0; i < count; i++)
        {
            var cube = Prim(PrimitiveType.Cube, debrisMat, true);
            float size = Random.Range(0.15f, 0.4f);
            cube.transform.position = pos + normal * 0.3f + Random.insideUnitSphere * 0.3f;
            cube.transform.localScale = Vector3.one * size;
            var rb = cube.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.linearVelocity = (normal + Random.insideUnitSphere * 0.8f).normalized * Random.Range(4f, 10f) * scale;
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            Object.Destroy(cube, 3f);
        }
    }

    /// Small soft fade, for shots that run out of range.
    public static void Fizzle(Vector3 pos, Color c)
    {
        var m = Additive(c, 1.5f);
        var puff = Prim(PrimitiveType.Sphere, m, false);
        puff.transform.position = pos;
        FxRunner.Run(GrowFade(puff.transform, m, 0.4f, 1.2f, 1.5f, 0.3f));
    }

    /// Missile smoke trail puff.
    public static void Smoke(Vector3 pos)
    {
        var m = Additive(new Color(0.8f, 0.8f, 0.8f), 0.25f);
        var puff = Prim(PrimitiveType.Sphere, m, false);
        puff.transform.position = pos;
        FxRunner.Run(GrowFade(puff.transform, m, 0.25f, 0.75f, 0.25f, 0.9f));
    }

    public static IEnumerator GrowFade(Transform t, Material m, float s0, float s1, float i0, float dur)
    {
        float time = 0f;
        while (time < dur)
        {
            if (t == null) yield break;
            time += Time.deltaTime;
            float k = Mathf.Clamp01(time / dur);
            t.localScale = Vector3.one * Mathf.Lerp(s0, s1, k);
            m.SetFloat("_Intensity", Mathf.Lerp(i0, 0f, k));
            yield return null;
        }
        if (t != null) Object.Destroy(t.gameObject);
    }

    public static IEnumerator FadeLight(Light l, float dur)
    {
        float start = l.intensity, time = 0f;
        while (time < dur)
        {
            if (l == null) yield break;
            time += Time.deltaTime;
            l.intensity = Mathf.Lerp(start, 0f, time / dur);
            yield return null;
        }
        if (l != null) Object.Destroy(l.gameObject);
    }

    // ------------------------------------------------------------ GUI

    /// Text with a black outline.
    public static void Label(Rect r, string text, GUIStyle style, Color color)
    {
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, color.a);
        for (int dx = -2; dx <= 2; dx += 2)
            for (int dy = -2; dy <= 2; dy += 2)
                if (dx != 0 || dy != 0)
                    GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), text, style);
        GUI.color = color;
        GUI.Label(r, text, style);
        GUI.color = old;
    }

    public static void Box(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    public static void Popup(Vector3 worldPos, string text, Color c) => Popups.Add(worldPos, text, c);
}

/// Runs effect coroutines for objects that can't run their own.
public class FxRunner : MonoBehaviour
{
    static FxRunner inst;
    public static void Run(IEnumerator routine)
    {
        if (inst == null) inst = new GameObject("FxRunner").AddComponent<FxRunner>();
        inst.StartCoroutine(routine);
    }
}

/// Floating damage numbers drawn with OnGUI.
public class Popups : MonoBehaviour
{
    class P { public Vector3 pos; public string text; public Color color; public float t; }

    const float Life = 0.7f;
    static Popups inst;
    readonly List<P> items = new List<P>();
    GUIStyle style;

    public static void Add(Vector3 pos, string text, Color color)
    {
        if (inst == null) inst = new GameObject("Popups").AddComponent<Popups>();
        inst.items.Add(new P { pos = pos + Random.insideUnitSphere * 0.4f, text = text, color = color });
    }

    void Update()
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            items[i].t += Time.deltaTime;
            items[i].pos += Vector3.up * 2f * Time.deltaTime;
            if (items[i].t > Life) items.RemoveAt(i);
        }
    }

    void OnGUI()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
        }
        foreach (var p in items)
        {
            Vector3 sp = cam.WorldToScreenPoint(p.pos);
            if (sp.z < 0) continue;
            var c = p.color;
            c.a = 1f - p.t / Life;
            Fx.Label(new Rect(sp.x - 60, Screen.height - sp.y - 20, 120, 40), p.text, style, c);
        }
    }
}
