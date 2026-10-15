using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Frieren-style flying mage.
///   WASD move (relative to where you look), Space = rise, Ctrl / C = descend
///   HOLD left click = charge Zoltraak (longer = more damage + range), release to fire
///   Automatic barrier blocks incoming shots (limited charges, recharges over time)
///   Getting hit = impact frames + hit-stop + launched backwards
///   V = first / third person    R = restart    Esc = free mouse
public class PlayerController : MonoBehaviour, IDamageable
{
    public static PlayerController Instance { get; private set; }

    // Flight
    const float FlySpeed = 12f;
    const float RiseSpeed = 9f;
    const float Accel = 60f;
    const float MouseSens = 0.12f;
    const int MaxHp = 100;

    // Zoltraak
    const float ChargeTime = 0.8f;
    const float FireCooldown = 0.12f;
    const float DmgMin = 15f, DmgMax = 70f;
    const float RangeMin = 40f, RangeMax = 180f;
    const float SpeedMin = 80f, SpeedMax = 130f;
    static readonly Color CircleColor = new Color(0.72f, 0.78f, 1f);

    // Auto-barrier
    const int BarrierMax = 6;
    const float BarrierRecharge = 3f;
    static readonly Color BarrierColor = new Color(0.65f, 0.9f, 1f);

    // Getting hit
    const float Knockback = 26f;
    const float KnockUp = 6f;
    const float StunTime = 0.35f;
    const float StunDrag = 40f;
    const float HitInvuln = 0.6f;

    // Camera
    const float TpDistance = 3.5f;
    const float TpShoulder = 0.6f;

    public Team Team => Team.Player;
    public bool Dead { get; private set; }
    public Vector3 Center => transform.position;

    CharacterController cc;
    Transform head;
    Camera cam;
    Renderer body;
    Vector3 velocity;
    Vector3 spawnPoint;
    float pitch, shake, damageFlash;
    int hp = MaxHp;

    bool charging;
    float charge, held, fireTimer;
    MagicCircle circle;

    int barrier = BarrierMax;
    float barrierTimer;

    float stun, invuln;
    bool thirdPerson;
    float tpBlend;

    Volume impactVolume;
    ColorAdjustments impactColor;
    GUIStyle big, small;

    public static PlayerController Create(Vector3 pos, float yaw)
    {
        var go = new GameObject("Player");
        go.transform.SetParent(Bootstrap.World, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var cc = go.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;
        cc.center = Vector3.zero;
        cc.minMoveDistance = 0f;
        return go.AddComponent<PlayerController>();
    }

    void Awake()
    {
        Instance = this;
        cc = GetComponent<CharacterController>();
        spawnPoint = transform.position;

        // Rig: Player (yaw) -> Head (pitch) -> Camera (pulled back in third person)
        head = new GameObject("Head").transform;
        head.SetParent(transform, false);
        head.localPosition = new Vector3(0f, 0.6f, 0f);

        var camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(head, false);
        cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 75f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1000f;
        cam.allowHDR = true;
        camGo.AddComponent<AudioListener>();
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        // Body (only visible in third person)
        var b = Fx.Prim(PrimitiveType.Capsule, Fx.Lit(new Color(0.92f, 0.92f, 0.97f)), false, transform);
        body = b.GetComponent<Renderer>();
        body.enabled = false;

        // Impact-frame post effect (stark black & white), off until you get hit
        var vgo = new GameObject("ImpactVolume");
        vgo.transform.SetParent(transform, false);
        impactVolume = vgo.AddComponent<Volume>();
        impactVolume.isGlobal = true;
        impactVolume.priority = 100f;
        impactVolume.weight = 0f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        impactVolume.sharedProfile = profile;
        impactColor = profile.Add<ColorAdjustments>(true);
        impactColor.saturation.Override(-100f);
        impactColor.contrast.Override(100f);
        impactColor.postExposure.Override(1.5f);

        LockMouse();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
    }

    static void LockMouse()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ------------------------------------------------------------ Loop

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;
        float dt = Time.deltaTime;

        if (kb.rKey.wasPressedThisFrame) { Bootstrap.Instance.Restart(); return; }
        if (kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        if (kb.vKey.wasPressedThisFrame) thirdPerson = !thirdPerson;

        bool locked = Cursor.lockState == CursorLockMode.Locked;
        if (!locked && mouse.leftButton.wasPressedThisFrame) { LockMouse(); return; }

        fireTimer = Mathf.Max(fireTimer - dt, 0f);
        invuln = Mathf.Max(invuln - dt, 0f);
        damageFlash = Mathf.Max(damageFlash - dt * 1.3f, 0f);

        if (!Dead && locked)
        {
            Vector2 d = mouse.delta.ReadValue() * MouseSens;
            transform.Rotate(0f, d.x, 0f);
            pitch = Mathf.Clamp(pitch - d.y, -89f, 89f);
            head.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            if (mouse.leftButton.wasPressedThisFrame) StartCharge();
        }
        if (mouse.leftButton.wasReleasedThisFrame) ReleaseCharge();

        if (charging)
        {
            held += dt;
            charge = Mathf.Min(charge + dt / ChargeTime, 1f);
            if (charge >= 1f) shake = Mathf.Max(shake, 0.015f);
        }

        if (barrier < BarrierMax)
        {
            barrierTimer += dt;
            if (barrierTimer >= BarrierRecharge) { barrierTimer = 0f; barrier++; }
        }
        else barrierTimer = 0f;

        Move(dt, kb);
    }

    void Move(float dt, Keyboard kb)
    {
        if (Dead || stun > 0f)
        {
            stun -= dt;
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, StunDrag * dt);
        }
        else
        {
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            float y = (kb.spaceKey.isPressed ? 1f : 0f) - ((kb.leftCtrlKey.isPressed || kb.cKey.isPressed) ? 1f : 0f);

            Vector3 wish = transform.right * x + transform.forward * z;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            wish *= FlySpeed;
            wish.y = y * RiseSpeed;
            velocity = Vector3.MoveTowards(velocity, wish, Accel * dt);
        }

        cc.Move(velocity * dt);

        if (transform.position.y < -20f)
        {
            cc.enabled = false;
            transform.position = spawnPoint;
            cc.enabled = true;
            velocity = Vector3.zero;
        }
    }

    void LateUpdate()
    {
        // Camera: first person at the head, third person pulled back over the shoulder
        tpBlend = Mathf.MoveTowards(tpBlend, thirdPerson ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        Vector3 local = new Vector3(TpShoulder * tpBlend, 0f, -TpDistance * tpBlend);
        if (tpBlend > 0.01f)
        {
            Vector3 dir = head.TransformPoint(local) - head.position;
            float len = dir.magnitude;
            if (Physics.SphereCast(head.position, 0.2f, dir / len, out RaycastHit hit, len, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
                local *= Mathf.Max(0.05f, (hit.distance - 0.1f) / len);
        }
        cam.transform.localPosition = local + Random.insideUnitSphere * shake * 0.5f;
        shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.5f);

        bool blink = invuln > 0f && (invuln % 0.1f) < 0.05f;
        body.enabled = tpBlend > 0.5f && !blink;

        if (charging) UpdateCircle();
    }

    // ------------------------------------------------------------ Aiming

    Vector3 Hand() => head.TransformPoint(new Vector3(0.28f, -0.18f, 0.45f));

    /// Where the crosshair points in the world.
    Vector3 AimPoint()
    {
        Vector3 from = cam.transform.position, dir = cam.transform.forward;
        Vector3 point = from + dir * 300f;
        float best = 300f;
        foreach (var h in Physics.RaycastAll(from, dir, 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best) { best = h.distance; point = h.point; }
        }
        return point;
    }

    // ------------------------------------------------------------ Zoltraak

    void StartCharge()
    {
        if (charging || fireTimer > 0f) return;
        charging = true;
        charge = 0f;
        held = 0f;
        circle = MagicCircle.Create(0.75f, CircleColor, 2f);
        circle.SetReveal(0f);
        UpdateCircle();
    }

    void UpdateCircle()
    {
        Vector3 dir = (AimPoint() - Hand()).normalized;
        circle.transform.position = Hand() + dir * 0.35f;
        circle.Face(dir);
        circle.SetReveal(Mathf.Min(1f, held / 0.15f));     // draws itself
        circle.SetIntensity(2f + 4f * charge);             // brighter as it charges
        circle.spinSpeed = 1f + 6f * charge;               // spins faster
        circle.SetScale(0.8f + 0.2f * charge);
    }

    void ReleaseCharge()
    {
        if (!charging) return;
        charging = false;
        fireTimer = FireCooldown;
        Fire(circle.transform.position, charge);
        circle.SetReveal(1f);
        circle.FadeOut(8f, 0.3f);
        circle = null;
    }

    void CancelCharge()
    {
        charging = false;
        if (circle != null) Destroy(circle.gameObject);
        circle = null;
    }

    void Fire(Vector3 origin, float power)
    {
        Vector3 dir = (AimPoint() - origin).normalized;
        var p = Projectile.Create(Projectile.Style.Beam, origin, dir * Mathf.Lerp(SpeedMin, SpeedMax, power), Team.Player, transform);
        p.core = Color.white;
        p.glow = new Color(0.5f, 0.88f, 1f);
        p.outer = new Color(0.55f, 0.3f, 1f);
        p.damage = Mathf.RoundToInt(Mathf.Lerp(DmgMin, DmgMax, power));
        p.maxDistance = Mathf.Lerp(RangeMin, RangeMax, power);
        p.hitScale = Mathf.Lerp(0.7f, 1.6f, power);
        p.lifetime = 4f;
        p.Launch();
        shake = Mathf.Max(shake, 0.03f + 0.07f * power);
    }

    /// Called by your Zoltraak when it hits something.
    public void OnZoltraakImpact(Vector3 point, float strength)
    {
        float dist = Vector3.Distance(transform.position, point);
        shake = Mathf.Max(shake, Mathf.Clamp(1f - dist / 60f, 0.15f, 1f) * 0.2f * strength);
        if (strength >= 1.55f) StartCoroutine(ImpactFrames());          // full charge = anime treatment
        else StartCoroutine(HitStop(0.03f + 0.04f * strength));
    }

    IEnumerator HitStop(float seconds)
    {
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(seconds);
        Time.timeScale = Dead ? 0.3f : 1f;
    }

    /// Two stark black & white frames (bright, then dark) during a near-freeze.
    IEnumerator ImpactFrames()
    {
        impactVolume.weight = 1f;
        impactColor.postExposure.value = 1.5f;
        Time.timeScale = 0.03f;
        yield return new WaitForSecondsRealtime(0.06f);
        impactColor.postExposure.value = -1.5f;
        yield return new WaitForSecondsRealtime(0.06f);
        impactVolume.weight = 0f;
        Time.timeScale = Dead ? 0.3f : 1f;
    }

    // ------------------------------------------------------------ Defense

    /// Called by enemy shots that get close. Returns true if the barrier blocked it.
    public bool TryBlock(Vector3 pos, Vector3 incomingVelocity)
    {
        if (Dead || invuln > 0f || barrier <= 0) return false;
        barrier--;
        var b = MagicCircle.Create(1.8f, BarrierColor, 4f);
        b.spinSpeed = 3f;
        b.transform.position = pos;
        b.Face(-incomingVelocity);
        b.PopAndFade(4f);
        shake = Mathf.Max(shake, 0.1f);
        return true;
    }

    /// Called by enemy shots that got through.
    public void TakeHit(int amount, Vector3 point, Vector3 direction)
    {
        if (Dead || invuln > 0f) return;
        hp = Mathf.Max(hp - amount, 0);
        invuln = HitInvuln;

        // Launched back along the shot's direction
        Vector3 dir = direction.sqrMagnitude > 0f ? direction.normalized : -transform.forward;
        velocity = dir * Knockback + Vector3.up * KnockUp;
        stun = StunTime;
        if (charging) CancelCharge();

        shake = Mathf.Max(shake, 0.35f);
        damageFlash = 0.4f;
        StartCoroutine(ImpactFrames());

        if (hp == 0) Die();
    }

    void Die()
    {
        Dead = true;
        if (charging) CancelCharge();
        StartCoroutine(DieRoutine());
    }

    IEnumerator DieRoutine()
    {
        yield return new WaitForSecondsRealtime(0.15f);
        Time.timeScale = 0.3f;   // dramatic slow-mo
        yield return new WaitForSecondsRealtime(1.5f);
        Time.timeScale = 1f;
        Bootstrap.Instance.Restart();
    }

    // ------------------------------------------------------------ HUD

    void OnGUI()
    {
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold };
            big.normal.textColor = Color.white;
            small = new GUIStyle(big) { fontSize = 18, fontStyle = FontStyle.Normal };
        }

        if (damageFlash > 0f)
            Fx.Box(new Rect(0, 0, Screen.width, Screen.height), new Color(1f, 0f, 0f, damageFlash));

        Fx.Label(new Rect(24, 20, 600, 40), $"HP  {hp} / {MaxHp}", big,
            Color.Lerp(new Color(1f, 0.3f, 0.3f), Color.white, hp / (float)MaxHp));
        Fx.Label(new Rect(24, 58, 600, 40), $"BARRIER  {barrier} / {BarrierMax}", big,
            barrier > 0 ? BarrierColor : new Color(1f, 0.4f, 0.3f));
        if (Dead)
            Fx.Label(new Rect(24, 96, 600, 40), "YOU DIED", big, new Color(1f, 0.3f, 0.3f));

        Fx.Label(new Rect(24, Screen.height - 44, 1200, 30),
            "Hold LMB: Zoltraak    WASD + Space / Ctrl: fly    V: view    R: restart    Esc: free mouse",
            small, new Color(1f, 1f, 1f, 0.7f));

        float cx = Screen.width / 2f, cy = Screen.height / 2f;
        Fx.Box(new Rect(cx - 3, cy - 3, 6, 6), new Color(1f, 1f, 1f, 0.9f));
        if (charging)
        {
            Fx.Box(new Rect(cx - 40, cy + 16, 80, 5), new Color(0f, 0f, 0f, 0.5f));
            Fx.Box(new Rect(cx - 40, cy + 16, 80 * charge, 5), charge >= 1f ? Color.white : CircleColor);
        }
    }
}
