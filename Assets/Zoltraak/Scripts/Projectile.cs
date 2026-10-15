using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Projectile used by everyone. Raycasts every frame so it never tunnels through walls.
///   Beam    = Zoltraak (player): long continuous beam from cast point to head,
///             white core / cyan glow / purple fringe; lingers and thins out after impact
///   Orb     = basic bolt
///   Missile = boss SAM: boosts up, then homes in with a limited turn rate, leaves smoke
public class Projectile : MonoBehaviour
{
    public enum Style { Beam, Orb, Missile }

    public const float BlockRange = 2.6f;   // how close enemy shots get before the auto-barrier triggers
    public const float BeamMaxLen = 40f;
    public const float BeamFade = 0.35f;

    public Style style = Style.Orb;
    public Team team;
    public Transform shooter;
    public float speed = 60f;
    public int damage = 20;
    public Color core = Color.white;
    public Color glow = new Color(0.5f, 0.88f, 1f);
    public Color outer = new Color(0.55f, 0.3f, 1f);
    public float maxDistance = 200f;
    public float lifetime = 4f;
    public float hitScale = 1f;
    public Vector3 velocity;

    // Homing (missiles)
    public Transform homingTarget;
    public float turnRate;      // radians / second
    public float boostTime;     // straight flight before homing

    float age, traveled, puffTimer;
    Vector3 origin;
    bool finished;
    Transform beamVis;
    readonly List<Transform> beamLayers = new List<Transform>();
    Light glowLight;

    public static Projectile Create(Style style, Vector3 pos, Vector3 velocity, Team team, Transform shooter)
    {
        var go = new GameObject(style.ToString());
        go.transform.SetParent(Bootstrap.World, false);
        go.transform.position = pos;
        var p = go.AddComponent<Projectile>();
        p.style = style;
        p.velocity = velocity;
        p.team = team;
        p.shooter = shooter;
        p.speed = velocity.magnitude;
        return p;
    }

    /// Call after setting fields: builds visuals.
    public void Launch()
    {
        origin = transform.position;
        switch (style)
        {
            case Style.Beam: BuildBeam(); break;
            case Style.Missile: BuildBall(0.18f, 0.9f); break;
            default: BuildBall(0.15f, 2f); break;
        }
        Face();
    }

    // ------------------------------------------------------------ Visuals

    void BuildBeam()
    {
        beamVis = new GameObject("BeamVis").transform;
        beamVis.SetParent(transform, false);
        AddLayer(0.07f, Fx.Glow(core, 12f));
        AddLayer(0.17f, Fx.Additive(glow, 4f));
        AddLayer(0.32f, Fx.Additive(outer, 2.5f));
        var tip = Fx.Prim(PrimitiveType.Sphere, Fx.Glow(core, 14f), false, beamVis);
        tip.transform.localScale = Vector3.one * 0.28f;
        tip.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        glowLight = Fx.PointLight(transform, transform.position, glow, 5f, 8f);
        SetBeamLength(0.01f);
    }

    void AddLayer(float radius, Material m)
    {
        var go = Fx.Prim(PrimitiveType.Cylinder, m, false, beamVis);
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lie along Z
        go.transform.localScale = new Vector3(radius * 2f, 0.005f, radius * 2f);
        beamLayers.Add(go.transform);
    }

    /// Stretch the beam back toward where it was cast (Unity: forward is +Z, so "behind" is -Z).
    void SetBeamLength(float length)
    {
        foreach (var t in beamLayers)
        {
            var s = t.localScale;
            t.localScale = new Vector3(s.x, Mathf.Max(length, 0.01f) * 0.5f, s.z);  // cylinder is 2 units tall
            t.localPosition = new Vector3(0f, 0f, -length * 0.5f);
        }
    }

    void BuildBall(float radius, float trailLength)
    {
        var ball = Fx.Prim(PrimitiveType.Sphere, Fx.Glow(core, 8f), false, transform);
        ball.transform.localScale = Vector3.one * radius * 2f;
        var trail = Fx.Prim(PrimitiveType.Cylinder, Fx.Glow(glow, 4f), false, transform);
        trail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        trail.transform.localScale = new Vector3(radius, trailLength * 0.5f, radius);
        trail.transform.localPosition = new Vector3(0f, 0f, -trailLength * 0.5f);
        glowLight = Fx.PointLight(transform, transform.position, core, 2.5f, 5f);
    }

    // ------------------------------------------------------------ Flight

    void Update()
    {
        if (finished) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        age += dt;
        if (age > lifetime || traveled > maxDistance) { Fizzle(); return; }

        Steer(dt);

        // Player's automatic barrier
        var pc = PlayerController.Instance;
        if (team == Team.Enemy && pc != null && !pc.Dead
            && Vector3.Distance(transform.position, pc.Center) < BlockRange
            && pc.TryBlock(transform.position, velocity))
        {
            Fx.Explosion(transform.position, -velocity.normalized, core, hitScale, false);
            Finish();
            return;
        }

        Vector3 from = transform.position;
        Vector3 step = velocity * dt;
        float dist = step.magnitude;

        if (dist > 0f && RaycastFirst(from, step / dist, dist, out RaycastHit hit))
        {
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null && target.Team != team)
                target.TakeHit(damage, hit.point, velocity.normalized);

            transform.position = hit.point;
            UpdateBeam();
            Fx.Explosion(hit.point, hit.normal, style == Style.Beam ? glow : core, hitScale, true);
            if (team == Team.Player && pc != null) pc.OnZoltraakImpact(hit.point, hitScale);
            Finish();
            return;
        }

        transform.position = from + step;
        traveled += dist;
        Face();
        UpdateBeam();

        if (style == Style.Missile)
        {
            puffTimer -= dt;
            if (puffTimer <= 0f) { puffTimer = 0.03f; Fx.Smoke(transform.position); }
        }
    }

    /// Closest hit that isn't the shooter.
    bool RaycastFirst(Vector3 from, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        bool found = false;
        float bestD = float.MaxValue;
        foreach (var h in Physics.RaycastAll(from, dir, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (shooter != null && h.collider.transform.IsChildOf(shooter)) continue;
            if (h.distance < bestD) { bestD = h.distance; best = h; found = true; }
        }
        return found;
    }

    /// SAM guidance: straight during boost, then turn toward the target at a limited rate.
    /// Forgiving: once it has flown past you, it loses lock for good.
    void Steer(float dt)
    {
        if (turnRate <= 0f || age < boostTime || homingTarget == null) return;
        var pc = homingTarget.GetComponent<PlayerController>();
        if (pc != null && pc.Dead) return;

        Vector3 desired = (homingTarget.position - transform.position).normalized;
        Vector3 cur = velocity.normalized;
        if (age > boostTime + 0.3f && Vector3.Angle(cur, desired) > 100f)
        {
            homingTarget = null;
            return;
        }
        cur = Vector3.RotateTowards(cur, desired, turnRate * dt, 0f);
        velocity = cur * speed;
    }

    void Face()
    {
        if (velocity.sqrMagnitude < 1e-6f) return;
        var d = velocity.normalized;
        transform.rotation = Quaternion.LookRotation(d, Mathf.Abs(d.y) < 0.99f ? Vector3.up : Vector3.forward);
    }

    void UpdateBeam()
    {
        if (style == Style.Beam)
            SetBeamLength(Mathf.Min(Vector3.Distance(transform.position, origin), BeamMaxLen));
    }

    void Fizzle()
    {
        if (style == Style.Beam) { Finish(); return; }
        Fx.Fizzle(transform.position, core);
        Destroy(gameObject);
    }

    /// End of the line. Beams linger and thin out; everything else just disappears.
    void Finish()
    {
        if (style != Style.Beam) { Destroy(gameObject); return; }
        finished = true;
        StartCoroutine(BeamFadeOut());
    }

    IEnumerator BeamFadeOut()
    {
        float t = 0f, startLight = glowLight != null ? glowLight.intensity : 0f;
        while (t < BeamFade)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / BeamFade);
            float thin = 1f - k * k;   // ease in
            beamVis.localScale = new Vector3(Mathf.Max(thin, 0.001f), Mathf.Max(thin, 0.001f), 1f);
            if (glowLight != null) glowLight.intensity = Mathf.Lerp(startLight, 0f, k);
            yield return null;
        }
        Destroy(gameObject);
    }
}
