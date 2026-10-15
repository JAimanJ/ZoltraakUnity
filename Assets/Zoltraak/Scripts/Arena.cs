using UnityEngine;
using UnityEngine.Rendering;

/// Builds a Frieren-style ruins arena in code:
/// grass field, stone plaza with a giant glowing carved magic circle, ring of ruined pillars
/// (intact / broken / fallen), stone arches, low broken walls for cover, rocks, trees,
/// and distant snowy mountains.
public static class Arena
{
    const float PlazaRadius = 22f;
    const float RingRadius = 34f;
    const int LayoutSeed = 7;   // change for a different layout

    static System.Random rng;
    static Material grass, stone, stoneDark, plaza, bark, mountain, snow;
    static Mesh cone;

    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    public static void Build(Transform parent)
    {
        rng = new System.Random(LayoutSeed);
        grass = Fx.Lit(new Color(0.38f, 0.52f, 0.3f), 0.05f);
        stone = Fx.Lit(new Color(0.74f, 0.72f, 0.67f), 0.1f);
        stoneDark = Fx.Lit(new Color(0.56f, 0.55f, 0.52f), 0.05f);
        plaza = Fx.Lit(new Color(0.66f, 0.65f, 0.62f), 0.15f);
        bark = Fx.Lit(new Color(0.35f, 0.26f, 0.2f), 0.05f);
        mountain = Fx.Lit(new Color(0.46f, 0.52f, 0.62f), 0.05f);
        snow = Fx.Lit(new Color(0.93f, 0.95f, 1f), 0.3f);
        cone = Fx.Cone(10);

        var root = new GameObject("Arena").transform;
        root.SetParent(parent, false);

        // Ground
        Box(root, new Vector3(500, 1, 500), new Vector3(0, -0.5f, 0), Quaternion.identity, grass, true);

        // Plaza: rim + stone disc + carved magic circle
        Cyl(root, PlazaRadius + 0.6f, 0.08f, new Vector3(0, 0.0f, 0), Quaternion.identity, stoneDark, false);
        Cyl(root, PlazaRadius, 0.1f, new Vector3(0, 0.02f, 0), Quaternion.identity, plaza, false);
        var carved = MagicCircle.Create(PlazaRadius * 1.75f, new Color(0.7f, 0.85f, 1f), 0.35f, root);
        carved.spinSpeed = 0.04f;
        carved.transform.position = new Vector3(0, 0.09f, 0);
        carved.Face(Vector3.up);

        PillarRing(root);
        Arches(root);
        CoverWalls(root);
        Rocks(root);
        Trees(root);
        Mountains(root);
    }

    // ------------------------------------------------------------ Pieces

    static void PillarRing(Transform root)
    {
        const int count = 16;
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.PI * 2f * i / count + R(-0.05f, 0.05f);
            Vector3 pos = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (RingRadius + R(-1f, 1f));
            float roll = R(0f, 1f);
            if (roll < 0.15f) continue;                   // gap
            else if (roll < 0.4f) IntactPillar(root, pos);
            else if (roll < 0.55f) FallenPillar(root, pos);
            else BrokenPillar(root, pos, R(2f, 7f));
        }
    }

    static void IntactPillar(Transform root, Vector3 pos)
    {
        const float h = 9f;
        Box(root, new Vector3(2.6f, 0.8f, 2.6f), pos + Vector3.up * 0.4f, Quaternion.identity, stoneDark, true);
        Cyl(root, 0.9f, h, pos + Vector3.up * (0.8f + h / 2f), Quaternion.identity, stone, true);
        Box(root, new Vector3(2.6f, 0.6f, 2.6f), pos + Vector3.up * (0.8f + h + 0.3f), Quaternion.identity, stoneDark, true);
    }

    static void BrokenPillar(Transform root, Vector3 pos, float h)
    {
        Box(root, new Vector3(2.6f, 0.8f, 2.6f), pos + Vector3.up * 0.4f, Quaternion.identity, stoneDark, true);
        Cyl(root, 0.9f, h, pos + Vector3.up * (0.8f + h / 2f), Quaternion.Euler(R(-3f, 3f), 0, R(-3f, 3f)), stone, true);
        // A chunk that broke off, lying nearby
        Vector3 chunkPos = pos + new Vector3(R(-3f, 3f), 0.9f, R(-3f, 3f));
        Cyl(root, 0.9f, R(1f, 2.5f), chunkPos, Quaternion.Euler(0, R(0f, 360f), 90f), stone, true);
    }

    static void FallenPillar(Transform root, Vector3 pos)
    {
        Box(root, new Vector3(2.6f, 0.8f, 2.6f), pos + Vector3.up * 0.4f, Quaternion.identity, stoneDark, true);
        float length = R(6f, 9f);
        float yaw = R(0f, 360f);
        Quaternion rot = Quaternion.Euler(0, yaw, 90f);
        Vector3 axis = rot * Vector3.up;     // the cylinder's long axis after tipping over
        axis.y = 0; axis.Normalize();
        Cyl(root, 0.9f, length, pos + axis * (length / 2f + 1.5f) + Vector3.up * 0.9f, rot, stone, true);
    }

    static void Arches(Transform root)
    {
        foreach (float deg in new[] { 30f, 150f, 270f })
        {
            float a = deg * Mathf.Deg2Rad;
            Vector3 center = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (RingRadius + 5f);
            Vector3 tangent = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
            bool broken = deg == 270f;
            Quaternion along = Quaternion.FromToRotation(Vector3.right, tangent);

            ArchLeg(root, center + tangent * 2.8f, 7f);
            ArchLeg(root, center - tangent * 2.8f, broken ? 3.5f : 7f);
            var lintel = new Vector3(7.6f, 1.2f, 1.8f);
            if (broken)
                Box(root, lintel, center + new Vector3(0, 0.7f, 0) + Vector3.Cross(Vector3.up, tangent) * 2f,
                    along * Quaternion.Euler(0, 17f, 8f), stoneDark, true);
            else
                Box(root, lintel, center + Vector3.up * (7f + 0.8f + 0.6f), along, stoneDark, true);
        }
    }

    static void ArchLeg(Transform root, Vector3 pos, float h)
    {
        Box(root, new Vector3(2f, 0.8f, 2f), pos + Vector3.up * 0.4f, Quaternion.identity, stoneDark, true);
        Cyl(root, 0.75f, h, pos + Vector3.up * (0.8f + h / 2f), Quaternion.identity, stone, true);
    }

    static void CoverWalls(Transform root)
    {
        const int count = 5;
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.PI * 2f * i / count + R(-0.2f, 0.2f) + 0.3f;
            Vector3 pos = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * R(11f, 15f);
            Vector3 tangent = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
            Quaternion along = Quaternion.FromToRotation(Vector3.right, tangent);
            float w = R(4f, 7f), h = R(1.8f, 2.8f);
            Box(root, new Vector3(w, h, 1f), pos + Vector3.up * (h / 2f), along, stoneDark, true);
            // Jagged broken top
            float topW = w * R(0.3f, 0.6f);
            Box(root, new Vector3(topW, 0.8f, 1f), pos + Vector3.up * (h + 0.4f) + tangent * R(-w / 4f, w / 4f),
                along, stoneDark, true);
        }
    }

    static void Rocks(Transform root)
    {
        for (int i = 0; i < 30; i++)
        {
            float a = R(0f, Mathf.PI * 2f), d = R(8f, 70f), s = R(0.5f, 1.8f);
            var size = new Vector3(s * R(0.8f, 1.5f), s, s * R(0.8f, 1.5f));
            Box(root, size, new Vector3(Mathf.Cos(a) * d, s * 0.3f, Mathf.Sin(a) * d),
                Quaternion.Euler(R(-20f, 20f), R(0f, 360f), R(-20f, 20f)), stoneDark, true);
        }
    }

    static void Trees(Transform root)
    {
        for (int i = 0; i < 45; i++)
        {
            float a = R(0f, Mathf.PI * 2f), d = R(46f, 100f);
            Vector3 pos = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            float h = R(3.5f, 6f);
            Cyl(root, 0.35f, h, pos + Vector3.up * (h / 2f), Quaternion.identity, bark, true);

            var leaf = Fx.Lit(Color.Lerp(new Color(0.24f, 0.42f, 0.22f), new Color(0.38f, 0.55f, 0.28f), R(0f, 1f)), 0.05f);
            float cr = R(2f, 3f);
            var crown = Fx.Prim(PrimitiveType.Sphere, leaf, false, root);
            crown.transform.position = pos + Vector3.up * (h + cr * 0.6f);
            crown.transform.localScale = new Vector3(cr * 2f, cr * 2f * R(1f, 1.4f), cr * 2f);
            var crown2 = Fx.Prim(PrimitiveType.Sphere, leaf, false, root);
            crown2.transform.position = pos + new Vector3(R(-1f, 1f), h + cr * 1.3f, R(-1f, 1f));
            crown2.transform.localScale = Vector3.one * cr * 1.4f;
        }
    }

    static void Mountains(Transform root)
    {
        for (int i = 0; i < 16; i++)
        {
            float a = Mathf.PI * 2f * i / 16f + R(-0.15f, 0.15f);
            float d = R(170f, 230f), r = R(40f, 70f), h = R(50f, 110f);
            Vector3 basePos = new Vector3(Mathf.Cos(a) * d, -5f, Mathf.Sin(a) * d);
            ConeObj(root, basePos, r * 2f, h, mountain);
            float hs = h * 0.3f;
            ConeObj(root, basePos + Vector3.up * (h - hs + 0.2f), r * 0.6f, hs, snow);   // snow cap
        }
    }

    // ------------------------------------------------------------ Helpers

    static GameObject Box(Transform root, Vector3 size, Vector3 pos, Quaternion rot, Material m, bool collider)
    {
        var go = Fx.Prim(PrimitiveType.Cube, m, collider, root);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = size;
        return go;
    }

    /// Unity's cylinder primitive is 2 units tall with diameter 1.
    static GameObject Cyl(Transform root, float radius, float height, Vector3 pos, Quaternion rot, Material m, bool collider)
    {
        var go = Fx.Prim(PrimitiveType.Cylinder, m, collider, root);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
        return go;
    }

    static void ConeObj(Transform root, Vector3 basePos, float diameter, float height, Material m)
    {
        var go = new GameObject("Mountain");
        go.transform.SetParent(root, false);
        go.transform.position = basePos;
        go.transform.localScale = new Vector3(diameter, height, diameter);
        go.AddComponent<MeshFilter>().sharedMesh = cone;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }
}
