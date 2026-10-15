using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// Procedural magic circle on a quad (see Shaders/MagicCircle.shader).
/// Used for Zoltraak casting, the auto-barrier, the boss, and the carved arena floor.
public class MagicCircle : MonoBehaviour
{
    public float spinSpeed = 1f;
    public float baseSize = 1f;

    Material mat;
    float spin;

    public static MagicCircle Create(float size, Color color, float intensity, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = "MagicCircle";
        go.transform.SetParent(parent != null ? parent : Bootstrap.World, false);
        go.transform.localScale = Vector3.one * size;

        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        var mc = go.AddComponent<MagicCircle>();
        mc.baseSize = size;
        mc.mat = new Material(Shader.Find("Zoltraak/MagicCircle"));
        r.sharedMaterial = mc.mat;
        mc.mat.SetColor("_Color", color);
        mc.SetIntensity(intensity);
        mc.SetReveal(1f);
        return mc;
    }

    void Update()
    {
        spin += Time.deltaTime * spinSpeed;
        mat.SetFloat("_Spin", spin);
    }

    public void SetIntensity(float v) => mat.SetFloat("_Intensity", v);
    public void SetReveal(float v) => mat.SetFloat("_Reveal", v);
    public void SetScale(float k) => transform.localScale = Vector3.one * baseSize * k;

    /// Point the circle's face along a direction.
    public void Face(Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        var up = Mathf.Abs(dir.normalized.y) < 0.99f ? Vector3.up : Vector3.forward;
        transform.rotation = Quaternion.LookRotation(dir, up);
    }

    /// Flare/fade from an intensity to 0, then destroy.
    public void FadeOut(float from, float duration) => StartCoroutine(FadeRoutine(from, duration));

    IEnumerator FadeRoutine(float from, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            SetIntensity(Mathf.Lerp(from, 0f, t / duration));
            yield return null;
        }
        Destroy(gameObject);
    }

    /// Barrier-style: pop open, hold, fade, destroy.
    public void PopAndFade(float intensity) => StartCoroutine(PopRoutine(intensity));

    IEnumerator PopRoutine(float intensity)
    {
        float t = 0f;
        while (t < 0.08f)
        {
            t += Time.deltaTime;
            float k = t / 0.08f;
            SetScale(Mathf.Lerp(0.3f, 1f, 1f - (1f - k) * (1f - k)));   // ease out
            yield return null;
        }
        SetScale(1f);
        yield return new WaitForSeconds(0.15f);
        yield return FadeRoutine(intensity, 0.3f);
    }
}
