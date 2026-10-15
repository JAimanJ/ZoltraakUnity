using System.Collections;
using UnityEngine;

/// Boss: big floating sphere with a magic circle beneath it.
/// Flies around you in 3D, glows as a warning, then launches a SAM-style missile barrage:
/// missiles boost up, then curve in with a limited turn rate (and lose lock once they overshoot).
public class Boss : MonoBehaviour, IDamageable
{
    const int MaxHp = 1200;
    const float Radius = 3f;
    static readonly Color BodyColor = new Color(0.16f, 0.1f, 0.2f);
    static readonly Color GlowColor = new Color(1f, 0.3f, 0.35f);

    // Flight
    const float FlySpeed = 11f;
    const float FlyAccel = 1.5f;
    const float KeepMin = 18f, KeepMax = 32f;
    const float MinAlt = 5f, MaxAlt = 28f;
    const float ArenaLimit = 48f;
    const float Reposition = 2.2f;

    // Barrage
    const float BarrageInterval = 3f;
    const float Windup = 0.5f;
    const int Missiles = 4;
    const float MissileGap = 0.12f;
    const float MissileSpeed = 22f;
    const float MissileTurn = 1.8f;    // rad/s, lower = easier to dodge
    const float MissileBoost = 0.4f;
    const int MissileDamage = 10;

    const float RespawnTime = 6f;      // test mode

    public Team Team => Team.Enemy;

    int hp = MaxHp;
    bool alive = true;
    Vector3 home, goal, vel;
    float t, barrageTimer = 2f, repositionTimer, flashT;
    Transform body;
    Collider col;
    Material mat;
    MagicCircle circle;
    string status = "";
    GUIStyle nameStyle, statusStyle;

    public static Boss Create(Vector3 pos)
    {
        var go = new GameObject("Boss");
        go.transform.SetParent(Bootstrap.World, false);
        go.transform.position = pos;
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        return go.AddComponent<Boss>();
    }

    void Awake()
    {
        home = transform.position;
        goal = home;

        mat = Fx.Lit(BodyColor, 0.6f);
        mat.SetFloat("_Metallic", 0.3f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", GlowColor * 0.3f);

        var sphere = Fx.Prim(PrimitiveType.Sphere, mat, true, transform);
        sphere.transform.localScale = Vector3.one * Radius * 2f;
        body = sphere.transform;
        col = sphere.GetComponent<Collider>();

        circle = MagicCircle.Create(Radius * 3f, GlowColor, 1.5f, transform);
        circle.spinSpeed = 0.5f;
        circle.transform.localPosition = new Vector3(0f, -Radius - 0.6f, 0f);
        circle.Face(Vector3.up);
    }

    void Update()
    {
        if (!alive) return;
        var player = PlayerController.Instance;
        if (player == null) return;
        float dt = Time.deltaTime;
        t += dt;

        Fly(dt, player);

        if (flashT > 0f)
        {
            flashT -= dt;
            mat.SetColor("_BaseColor", Color.Lerp(BodyColor, Color.white, Mathf.Clamp01(flashT / 0.15f)));
        }

        if (player.Dead) return;

        // Warning glow ramps up before each barrage
        barrageTimer -= dt;
        float k = barrageTimer < Windup ? 1f - Mathf.Max(barrageTimer, 0f) / Windup : 0f;
        mat.SetColor("_EmissionColor", GlowColor * (0.3f + 3.7f * k));
        circle.SetIntensity(1.5f + 3.5f * k);

        if (barrageTimer <= 0f)
        {
            barrageTimer = BarrageInterval;
            StartCoroutine(Barrage(player));
        }
    }

    /// Keeps repositioning around the player in 3D: circles, changes altitude, keeps its distance.
    void Fly(float dt, PlayerController player)
    {
        repositionTimer -= dt;
        if (repositionTimer <= 0f || Vector3.Distance(transform.position, goal) < 2f)
        {
            repositionTimer = Reposition + Random.Range(-0.5f, 0.5f);
            Vector3 p = player.transform.position;
            float ang = Random.value * Mathf.PI * 2f;
            float dist = Random.Range(KeepMin, KeepMax);
            Vector2 flat = new Vector2(p.x + Mathf.Cos(ang) * dist, p.z + Mathf.Sin(ang) * dist);
            if (flat.magnitude > ArenaLimit) flat = flat.normalized * ArenaLimit;
            goal = new Vector3(flat.x, Mathf.Clamp(p.y + Random.Range(2f, 12f), MinAlt, MaxAlt), flat.y);
        }

        Vector3 desired = Vector3.ClampMagnitude(goal - transform.position, FlySpeed);
        vel = Vector3.Lerp(vel, desired, Mathf.Clamp01(FlyAccel * dt));
        transform.position += (vel + Vector3.up * Mathf.Sin(t * 2f) * 0.4f) * dt;
    }

    IEnumerator Barrage(PlayerController player)
    {
        float spin = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < Missiles; i++)
        {
            if (!alive || player == null) yield break;
            FireMissile(spin + Mathf.PI * 2f * i / Missiles, player);
            yield return new WaitForSeconds(MissileGap);
        }
    }

    void FireMissile(float angle, PlayerController player)
    {
        Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 origin = transform.position + Vector3.up * Radius * 0.9f + outward * Radius * 0.5f;
        Vector3 dir = (Vector3.up + outward * 0.6f).normalized;   // vertical launch, fanned out

        var m = Projectile.Create(Projectile.Style.Missile, origin, dir * MissileSpeed * 0.7f, Team.Enemy, transform);
        m.speed = MissileSpeed;
        m.damage = MissileDamage;
        m.core = new Color(1f, 0.35f, 0.3f);
        m.glow = new Color(1f, 0.55f, 0.2f);
        m.homingTarget = player.transform;
        m.turnRate = MissileTurn;
        m.boostTime = MissileBoost;
        m.maxDistance = 400f;
        m.lifetime = 8f;
        m.Launch();
    }

    public void TakeHit(int amount, Vector3 point, Vector3 direction)
    {
        if (!alive) return;
        hp = Mathf.Max(hp - amount, 0);
        flashT = 0.15f;
        Fx.Popup(point, amount.ToString(), new Color(1f, 0.85f, 0.3f));
        if (hp == 0) Die();
    }

    void Die()
    {
        alive = false;
        col.enabled = false;
        circle.gameObject.SetActive(false);
        status = "BOSS DEFEATED";

        var burst = MagicCircle.Create(Radius * 6f, Color.white, 8f);
        burst.spinSpeed = 4f;
        burst.transform.position = transform.position;
        if (Camera.main != null) burst.Face(Camera.main.transform.position - transform.position);
        burst.FadeOut(8f, 1f);

        StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        float d = 0f;
        while (d < 0.45f)
        {
            d += Time.deltaTime;
            float s = d < 0.15f ? Mathf.Lerp(1f, 1.3f, d / 0.15f) : Mathf.Lerp(1.3f, 0f, (d - 0.15f) / 0.3f);
            body.localScale = Vector3.one * Radius * 2f * s;
            yield return null;
        }
        body.localScale = Vector3.zero;

        yield return new WaitForSeconds(RespawnTime);

        // Respawn (test mode)
        hp = MaxHp;
        alive = true;
        status = "";
        barrageTimer = 2f;
        vel = Vector3.zero;
        transform.position = home;
        goal = home;
        mat.SetColor("_BaseColor", BodyColor);
        circle.gameObject.SetActive(true);
        col.enabled = true;
        d = 0f;
        while (d < 0.5f)
        {
            d += Time.deltaTime;
            float k = d / 0.5f;
            body.localScale = Vector3.one * Radius * 2f * (1f + 0.3f * Mathf.Sin(k * Mathf.PI)) * Mathf.Min(1f, k * 1.5f);
            yield return null;
        }
        body.localScale = Vector3.one * Radius * 2f;
    }

    void OnGUI()
    {
        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            nameStyle.normal.textColor = Color.white;
            statusStyle = new GUIStyle(nameStyle) { fontSize = 64 };
        }

        float cx = Screen.width / 2f;
        if (alive)
        {
            Fx.Label(new Rect(cx - 320, 16, 640, 30), "THE SPHERE", nameStyle, Color.white);
            Fx.Box(new Rect(cx - 320, 50, 640, 14), new Color(0f, 0f, 0f, 0.6f));
            Fx.Box(new Rect(cx - 320, 50, 640 * hp / (float)MaxHp, 14), GlowColor);
        }
        if (status.Length > 0)
            Fx.Label(new Rect(0, 0, Screen.width, Screen.height), status, statusStyle, Color.white);
    }
}
