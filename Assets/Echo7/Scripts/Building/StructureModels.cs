using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Процедурные модели построек в стиле референсов: светло-серые модульные корпуса со скошенными гранями,
    /// тёмные вставки, оранжевые световые полосы, голубые излучатели.
    /// Узлы с именем, начинающимся на '#', — подвижные (их ищут скрипты через Model.Find).
    /// Замена на реальные модели: подставьте Instantiate(prefab, mb.Root) в нужный метод.
    /// </summary>
    public static class StructureModels
    {
        static Material L => Palette.HullLight;
        static Material M => Palette.HullMid;
        static Material D => Palette.HullDark;
        static Material O => Palette.Orange;
        static Material B => Palette.Blue;

        public static void Reactor(ModelBuilder mb, BuildingDef def)
        {
            mb.Hex(new Vector3(0, 0.3f, 0), 4.1f, 0.6f, Palette.Concrete);
            mb.Hex(new Vector3(0, 0.75f, 0), 3.4f, 0.3f, D);
            mb.Cyl(new Vector3(0, 2.2f, 0), 1.8f, 2.6f, L, null, null, 24);
            mb.Cyl(new Vector3(0, 2.0f, 0), 1.83f, 0.35f, O, null, null, 24);
            mb.Cyl(new Vector3(0, 3.7f, 0), 1.5f, 0.4f, M, null, null, 24);
            mb.Sphere(new Vector3(0, 4.0f, 0), new Vector3(2.6f, 1.4f, 2.6f), L);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 3.0f;
                mb.Box(p + Vector3.up * 2.2f, new Vector3(0.8f, 4.4f, 0.8f), M, 0.12f);
                mb.Strip(p + Vector3.up * 2.4f + (Vector3.zero - p).normalized * 0.41f, new Vector3(0.12f, 3.2f, 0.12f), O);
                // трубопровод к ядру
                var mid = (p * 0.55f) + Vector3.up * 1.4f;
                mb.Cyl(mid, 0.16f, p.magnitude * 0.8f, Palette.Metal, null, Quaternion.LookRotation(-p).eulerAngles + new Vector3(90, 0, 0), 10);
            }
            var ring = mb.Group("#ring", new Vector3(0, 2.9f, 0));
            mb.Torus(Vector3.zero, 2.45f, 0.11f, O, ring);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                mb.Box(new Vector3(Mathf.Cos(a) * 2.45f, 0, Mathf.Sin(a) * 2.45f), new Vector3(0.45f, 0.3f, 0.45f), D, 0.05f, ring);
            }
            var core = mb.Group("#core", new Vector3(0, 5.1f, 0));
            mb.Sphere(Vector3.zero, Vector3.one * 0.9f, O, core);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                mb.Box(new Vector3(Mathf.Cos(a) * 0.62f, 0, Mathf.Sin(a) * 0.62f), new Vector3(0.1f, 1.4f, 0.1f), D, 0f);
                // каркас клетки — статический (сливается)
            }
            mb.PointLight(new Vector3(0, 5.5f, 0), new Color(1f, 0.6f, 0.3f), 16f, 2.2f);
        }

        public static void Command(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 1.25f, 0), new Vector3(5.6f, 2.5f, 5.6f), L, 0.25f);
            mb.Box(new Vector3(0, 0.15f, 0), new Vector3(5.9f, 0.3f, 5.9f), D, 0.05f);
            mb.Box(new Vector3(0, 3.5f, 0), new Vector3(4.2f, 2.0f, 4.2f), M, 0.2f);
            mb.Box(new Vector3(0, 3.65f, 0), new Vector3(4.28f, 0.55f, 4.28f), Palette.Glass, 0.05f);
            mb.Box(new Vector3(0, 4.6f, 0), new Vector3(4.7f, 0.25f, 4.7f), D, 0.08f);
            foreach (var s in new[] { 1, -1 })
            {
                mb.Strip(new Vector3(0, 0.7f, s * 2.81f), new Vector3(4.6f, 0.1f, 0.05f), O);
                mb.Strip(new Vector3(s * 2.81f, 0.7f, 0), new Vector3(0.05f, 0.1f, 4.6f), O);
                mb.Box(new Vector3(s * 1.6f, 5.0f, -0.8f), new Vector3(0.9f, 0.6f, 0.9f), M, 0.08f);
            }
            mb.Box(new Vector3(0, 1.0f, 2.82f), new Vector3(1.4f, 1.9f, 0.12f), D, 0.03f);
            mb.Strip(new Vector3(0, 2.05f, 2.86f), new Vector3(1.5f, 0.08f, 0.05f), O);
            mb.Cyl(new Vector3(1.5f, 7.5f, 1.5f), 0.07f, 6f, Palette.Metal, null, null, 6);
            mb.Frustum(new Vector3(-1.4f, 5.15f, 1.3f), 0.05f, 0.7f, 0.5f, L, null, new Vector3(-30, 0, 0), 12);
            var beacon = mb.Group("#beacon", new Vector3(1.5f, 10.6f, 1.5f));
            mb.Sphere(Vector3.zero, Vector3.one * 0.25f, Palette.Red, beacon);
            mb.PointLight(new Vector3(0, 2.6f, 3.6f), new Color(1f, 0.7f, 0.45f), 9f, 1.2f);
        }

        public static void Habitat(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.15f, 0), new Vector3(5.8f, 0.3f, 5.6f), D, 0.05f);
            mb.Box(new Vector3(0, 1.45f, 0), new Vector3(5.4f, 2.4f, 4.0f), L, 0.45f);
            mb.Box(new Vector3(0, 2.75f, -0.3f), new Vector3(3.6f, 0.6f, 2.4f), M, 0.15f);
            mb.Box(new Vector3(0, 1.75f, 2.0f), new Vector3(4.2f, 0.45f, 0.08f), Palette.Glass, 0.02f);
            mb.Strip(new Vector3(0, 1.4f, 2.03f), new Vector3(4.2f, 0.07f, 0.05f), O);
            mb.Box(new Vector3(2.0f, 1.0f, 2.25f), new Vector3(1.1f, 1.8f, 0.6f), M, 0.1f);
            mb.Box(new Vector3(2.0f, 0.95f, 2.56f), new Vector3(0.8f, 1.6f, 0.04f), D, 0.01f);
            mb.Box(new Vector3(-1.2f, 3.2f, -0.8f), new Vector3(1.6f, 0.06f, 1.0f), MaterialFactory.Cached("solar", () => MaterialFactory.Lit(new Color(0.08f, 0.14f, 0.3f), 0.4f, 0.9f)), 0f, null, new Vector3(-20, 0, 0));
            mb.Box(new Vector3(-2.75f, 1.4f, 0), new Vector3(0.3f, 1.6f, 2.2f), M, 0.08f);
            mb.PointLight(new Vector3(2.0f, 2.3f, 3.0f), new Color(1f, 0.75f, 0.5f), 7f, 1f);
        }

        public static void MedBay(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.15f, 0), new Vector3(5.8f, 0.3f, 5.8f), D, 0.05f);
            mb.Box(new Vector3(0, 1.4f, 0), new Vector3(5.0f, 2.3f, 5.0f), L, 0.5f);
            mb.Box(new Vector3(0, 2.7f, 0), new Vector3(3.4f, 0.4f, 3.4f), L, 0.15f);
            mb.Strip(new Vector3(0, 2.92f, 0), new Vector3(2.4f, 0.06f, 0.6f), Palette.Red);
            mb.Strip(new Vector3(0, 2.92f, 0), new Vector3(0.6f, 0.06f, 2.4f), Palette.Red);
            mb.Box(new Vector3(0, 1.6f, 2.5f), new Vector3(3.6f, 0.5f, 0.06f), Palette.Glass, 0.01f);
            mb.Strip(new Vector3(0, 1.25f, 2.53f), new Vector3(3.6f, 0.06f, 0.04f), B);
            mb.Box(new Vector3(-1.9f, 0.95f, 2.52f), new Vector3(0.9f, 1.6f, 0.05f), D, 0.01f);
        }

        public static void Storage(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.1f, 0), new Vector3(3.8f, 0.2f, 3.8f), D, 0.04f);
            mb.Box(new Vector3(0, 1.2f, -0.5f), new Vector3(3.6f, 2.0f, 2.2f), M, 0.12f);
            for (int i = -2; i <= 2; i++) mb.Box(new Vector3(i * 0.7f, 1.2f, 0.62f), new Vector3(0.1f, 1.9f, 0.06f), D, 0.01f);
            mb.Strip(new Vector3(0, 2.15f, 0.63f), new Vector3(3.2f, 0.06f, 0.04f), O);
            mb.Box(new Vector3(-1.1f, 0.5f, 1.3f), new Vector3(0.9f, 0.8f, 0.9f), Palette.Hazard, 0.06f);
            mb.Box(new Vector3(0.2f, 0.45f, 1.35f), new Vector3(0.8f, 0.7f, 0.8f), D, 0.06f);
            mb.Box(new Vector3(-1.1f, 1.2f, 1.3f), new Vector3(0.7f, 0.6f, 0.7f), L, 0.06f);
        }

        public static void Extractor(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.25f, 0), new Vector3(3.4f, 0.5f, 3.4f), D, 0.08f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                mb.Box(dir * 0.9f + Vector3.up * 2.1f, new Vector3(0.18f, 3.6f, 0.18f), M, 0.03f, null, new Vector3(0, a, 0) + Quaternion.FromToRotation(Vector3.up, (Vector3.up * 3.6f - dir * 1.6f).normalized).eulerAngles);
            }
            mb.Box(new Vector3(0, 3.9f, 0), new Vector3(1.0f, 0.8f, 1.0f), L, 0.12f);
            mb.Strip(new Vector3(0, 3.9f, 0.51f), new Vector3(0.6f, 0.08f, 0.04f), O);
            var drill = mb.Group("#drill", new Vector3(0, 1.8f, 0));
            mb.Cyl(Vector3.zero, 0.18f, 3.4f, Palette.Metal, drill, null, 8);
            mb.Frustum(new Vector3(0, -1.6f, 0), 0.05f, 0.4f, 0.6f, D, drill, null, 8);
            for (int i = 0; i < 3; i++) mb.Box(new Vector3(0, -0.8f + i * 0.6f, 0), new Vector3(0.6f, 0.08f, 0.12f), O, 0f, drill, new Vector3(0, i * 60f, 0));
            mb.Box(new Vector3(1.3f, 0.8f, -1.2f), new Vector3(0.8f, 0.6f, 0.6f), Palette.Hazard, 0.05f);
        }

        public static void Condenser(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.15f, 0), new Vector3(3.4f, 0.3f, 3.4f), D, 0.04f);
            mb.Cyl(new Vector3(0, 1.8f, 0), 1.05f, 3.0f, L, null, null, 20);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                mb.Box(dir * 1.12f + Vector3.up * 1.8f, new Vector3(0.06f, 2.6f, 0.35f), M, 0f, null, new Vector3(0, a, 0));
            }
            mb.Cyl(new Vector3(0, 1.4f, 0), 1.08f, 0.15f, B, null, null, 20);
            mb.Frustum(new Vector3(0, 3.55f, 0), 1.05f, 0.35f, 0.5f, M, null, null, 20);
            mb.Cyl(new Vector3(1.3f, 0.5f, 1.2f), 0.3f, 0.7f, B, null, null, 10);
        }

        public static void DroneBay(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.2f, 0), new Vector3(5.6f, 0.4f, 5.6f), D, 0.08f);
            mb.Torus(new Vector3(0.6f, 0.42f, 0.6f), 1.7f, 0.07f, B);
            mb.Hex(new Vector3(0.6f, 0.43f, 0.6f), 1.2f, 0.04f, M);
            mb.Box(new Vector3(-1.7f, 1.4f, -1.7f), new Vector3(2.0f, 2.4f, 2.0f), L, 0.2f);
            mb.Strip(new Vector3(-1.7f, 2.0f, -0.69f), new Vector3(1.4f, 0.08f, 0.04f), O);
            mb.Cyl(new Vector3(-2.3f, 3.3f, -2.3f), 0.05f, 1.6f, Palette.Metal, null, null, 6);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                mb.Sphere(new Vector3(0.6f + Mathf.Cos(a) * 2.2f, 0.5f, 0.6f + Mathf.Sin(a) * 2.2f), Vector3.one * 0.18f, B);
            }
        }

        public static void Solar(ModelBuilder mb, BuildingDef def)
        {
            var panel = MaterialFactory.Cached("solar", () => MaterialFactory.Lit(new Color(0.08f, 0.14f, 0.3f), 0.4f, 0.9f));
            mb.Box(new Vector3(0, 0.1f, 0), new Vector3(1.2f, 0.2f, 1.2f), D, 0.03f);
            mb.Cyl(new Vector3(0, 0.8f, 0), 0.12f, 1.4f, Palette.Metal, null, null, 8);
            var tilt = new Vector3(-25f, 0, 0);
            mb.Box(new Vector3(0, 1.55f, 0), new Vector3(3.6f, 0.1f, 2.6f), M, 0.02f, null, tilt);
            mb.Box(new Vector3(0, 1.62f, 0), new Vector3(3.4f, 0.04f, 2.4f), panel, 0f, null, tilt);
        }

        public static void Generator(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.15f, 0), new Vector3(3.6f, 0.3f, 3.6f), D, 0.04f);
            mb.Box(new Vector3(0, 1.3f, 0), new Vector3(3.0f, 2.0f, 3.0f), M, 0.25f);
            mb.Box(new Vector3(0, 1.3f, 1.52f), new Vector3(1.6f, 0.9f, 0.05f), O, 0.01f);
            mb.Cyl(new Vector3(-0.8f, 3.0f, -0.6f), 0.28f, 1.6f, D, null, null, 10);
            mb.Cyl(new Vector3(0.7f, 2.8f, -0.6f), 0.22f, 1.2f, D, null, null, 10);
            mb.Cyl(new Vector3(1.5f, 0.6f, 0), 0.18f, 1.6f, Palette.Metal, null, new Vector3(90, 0, 0), 8);
        }

        public static void Pylon(ModelBuilder mb, BuildingDef def)
        {
            mb.Hex(new Vector3(0, 0.2f, 0), 0.55f, 0.4f, D);
            mb.Cyl(new Vector3(0, 1.8f, 0), 0.12f, 2.8f, M, null, null, 8);
            for (int i = 0; i < 3; i++) mb.Cyl(new Vector3(0, 1.4f + i * 0.5f, 0), 0.2f, 0.1f, L, null, null, 10);
            mb.Sphere(new Vector3(0, 3.35f, 0), Vector3.one * 0.38f, B);
            mb.Strip(new Vector3(0, 0.42f, 0), new Vector3(0.7f, 0.04f, 0.7f), O);
        }

        public static void Gate(ModelBuilder mb, BuildingDef def)
        {
            foreach (var s in new[] { -1, 1 })
            {
                mb.Box(new Vector3(s * 0.95f, 1.8f, 0), new Vector3(0.45f, 3.6f, 0.9f), M, 0.08f);
                mb.Strip(new Vector3(s * 0.71f, 1.8f, 0), new Vector3(0.04f, 3.0f, 0.1f), O);
            }
            mb.Box(new Vector3(0, 3.55f, 0), new Vector3(2.4f, 0.5f, 0.95f), L, 0.08f);
            var door = mb.Group("#door", new Vector3(0, 1.6f, 0));
            mb.Box(Vector3.zero, new Vector3(1.5f, 3.0f, 0.25f), D, 0.04f, door);
            mb.Strip(new Vector3(0, -0.6f, 0.13f), new Vector3(1.3f, 0.08f, 0.03f), O, door);
            mb.Box(new Vector3(0, 0.5f, 0.13f), new Vector3(1.2f, 0.06f, 0.03f), Palette.Hazard, 0f, door);
        }

        static void WallPost(ModelBuilder mb, float h, Material body, bool glow)
        {
            mb.Hex(new Vector3(0, h * 0.5f, 0), 0.62f, h, body);
            mb.Hex(new Vector3(0, h + 0.1f, 0), 0.66f, 0.2f, M);
            if (glow) mb.Hex(new Vector3(0, h * 0.78f, 0), 0.64f, 0.07f, O);
        }

        public static void Fence(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.75f, 0), new Vector3(0.3f, 1.5f, 0.3f), Palette.Metal, 0.04f);
            mb.Sphere(new Vector3(0, 1.55f, 0), Vector3.one * 0.12f, O);
        }

        public static void Wall(ModelBuilder mb, BuildingDef def) => WallPost(mb, def.Height, Palette.Concrete, true);
        public static void TitaniumWall(ModelBuilder mb, BuildingDef def) => WallPost(mb, def.Height, Palette.Metal, true);

        /// <summary>Секция стены от центра клетки к соседу (половина длины — соседи встречаются посередине).</summary>
        public static void WallLink(ModelBuilder mb, BuildingDef def, Vector3 dir)
        {
            bool alongX = Mathf.Abs(dir.x) > 0.5f;
            Vector3 Size(float len, float h, float th) => alongX ? new Vector3(len, h, th) : new Vector3(th, h, len);
            var c = dir * 0.5f;
            switch (def.Kind)
            {
                case StructureKind.Fence:
                    mb.Box(c + Vector3.up * 0.55f, Size(1.02f, 0.08f, 0.08f), Palette.Metal, 0f);
                    mb.Box(c + Vector3.up * 1.2f, Size(1.02f, 0.08f, 0.08f), Palette.Metal, 0f);
                    mb.Box(c + Vector3.up * 0.85f, Size(1.02f, 0.9f, 0.03f), D, 0f);
                    break;
                case StructureKind.TitaniumWall:
                    mb.Box(c + Vector3.up * def.Height * 0.5f, Size(1.04f, def.Height, 1.1f), Palette.Metal, 0.12f);
                    mb.Box(c + Vector3.up * def.Height * 0.25f, Size(1.04f, 0.3f, 1.2f), D, 0.05f);
                    mb.Strip(c + Vector3.up * (def.Height - 0.4f), Size(1.04f, 0.08f, 1.14f), O);
                    break;
                default:
                    mb.Box(c + Vector3.up * def.Height * 0.5f, Size(1.04f, def.Height, 0.95f), Palette.Concrete, 0.14f);
                    mb.Box(c + Vector3.up * (def.Height - 0.2f), Size(1.04f, 0.25f, 1.05f), M, 0.05f);
                    mb.Strip(c + Vector3.up * def.Height * 0.62f, Size(1.04f, 0.06f, 0.99f), O);
                    break;
            }
        }

        static Transform TurretBase(ModelBuilder mb, float r, float h)
        {
            mb.Hex(new Vector3(0, h * 0.5f, 0), r, h, M);
            mb.Hex(new Vector3(0, h + 0.08f, 0), r * 0.75f, 0.16f, D);
            mb.Strip(new Vector3(0, h * 0.7f, r * 0.87f), new Vector3(r * 0.8f, 0.07f, 0.04f), O);
            return mb.Group("#head", new Vector3(0, h + 0.55f, 0));
        }

        public static void MgTurret(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.1f, 0), new Vector3(3.4f, 0.2f, 3.4f), D, 0.04f);
            var head = TurretBase(mb, 1.25f, 0.8f);
            mb.Box(Vector3.zero, new Vector3(1.2f, 0.75f, 1.3f), L, 0.15f, head);
            mb.Box(new Vector3(0, 0.45f, -0.2f), new Vector3(0.7f, 0.2f, 0.7f), M, 0.05f, head);
            mb.Strip(new Vector3(0, 0.12f, 0.66f), new Vector3(0.5f, 0.08f, 0.03f), O, head);
            mb.Box(new Vector3(0.75f, -0.05f, -0.1f), new Vector3(0.3f, 0.5f, 0.7f), Palette.Hazard, 0.04f, head);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.22f : 0.22f;
                mb.Cyl(new Vector3(x, 0.02f, 1.2f), 0.075f, 1.3f, Palette.Metal, head, new Vector3(90, 0, 0), 8);
                mb.Cyl(new Vector3(x, 0.02f, 1.85f), 0.1f, 0.15f, D, head, new Vector3(90, 0, 0), 8);
                mb.Group("muzzle" + i, new Vector3(x, 0.02f, 1.95f), head);
            }
        }

        public static void LaserTurret(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.1f, 0), new Vector3(3.4f, 0.2f, 3.4f), D, 0.04f);
            var head = TurretBase(mb, 1.2f, 0.9f);
            mb.Sphere(new Vector3(0, 0.1f, 0), new Vector3(1.2f, 0.85f, 1.4f), L, head);
            mb.Cyl(new Vector3(0, 0.1f, 0.95f), 0.22f, 1.1f, D, head, new Vector3(90, 0, 0), 12);
            mb.Torus(new Vector3(0, 0.1f, 0.8f), 0.3f, 0.05f, B, head, new Vector3(90, 0, 0));
            mb.Torus(new Vector3(0, 0.1f, 1.15f), 0.27f, 0.05f, B, head, new Vector3(90, 0, 0));
            mb.Sphere(new Vector3(0, 0.1f, 1.55f), Vector3.one * 0.26f, B, head);
            mb.Box(new Vector3(0, 0.6f, -0.2f), new Vector3(0.5f, 0.15f, 0.8f), M, 0.04f, head);
            mb.Group("muzzle0", new Vector3(0, 0.1f, 1.7f), head);
        }

        public static void HeavyTurret(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.12f, 0), new Vector3(5.4f, 0.25f, 5.4f), D, 0.06f);
            mb.Hex(new Vector3(0, 0.55f, 0), 2.3f, 0.9f, Palette.Concrete);
            var head = TurretBase(mb, 1.8f, 1.2f);
            head.localPosition += Vector3.up * 0.3f;
            mb.Box(Vector3.zero, new Vector3(2.2f, 1.3f, 2.5f), M, 0.25f, head);
            mb.Box(new Vector3(0, 0.25f, -0.2f), new Vector3(2.4f, 0.8f, 1.6f), L, 0.2f, head);
            mb.Strip(new Vector3(0, -0.2f, 1.26f), new Vector3(1.4f, 0.08f, 0.04f), O, head);
            mb.Cyl(new Vector3(0, 0.1f, 2.4f), 0.2f, 2.8f, Palette.Metal, head, new Vector3(90, 0, 0), 12);
            mb.Box(new Vector3(0, 0.1f, 3.85f), new Vector3(0.6f, 0.5f, 0.5f), D, 0.06f, head);
            mb.Group("muzzle0", new Vector3(0, 0.1f, 4.1f), head);
        }

        public static void Mine(ModelBuilder mb, BuildingDef def)
        {
            mb.Cyl(new Vector3(0, 0.07f, 0), 0.45f, 0.14f, D, null, null, 12);
            mb.Cyl(new Vector3(0, 0.17f, 0), 0.25f, 0.08f, Palette.Hazard, null, null, 12);
            var lamp = mb.Group("#lamp", new Vector3(0, 0.24f, 0));
            mb.Sphere(Vector3.zero, Vector3.one * 0.11f, Palette.Red, lamp);
        }

        public static void Sensor(ModelBuilder mb, BuildingDef def)
        {
            mb.Hex(new Vector3(0, 0.2f, 0), 0.7f, 0.4f, D);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                mb.Box(new Vector3(Mathf.Cos(a) * 0.3f, 2.4f, Mathf.Sin(a) * 0.3f), new Vector3(0.07f, 4.2f, 0.07f), Palette.Metal, 0f);
            }
            for (int i = 0; i < 4; i++) mb.Box(new Vector3(0, 1f + i, 0), new Vector3(0.7f, 0.05f, 0.7f), M, 0f);
            var dish = mb.Group("#dish", new Vector3(0, 4.6f, 0));
            mb.Frustum(new Vector3(0, 0, 0.2f), 0.05f, 0.75f, 0.35f, L, dish, new Vector3(-80, 0, 0), 14);
            mb.Sphere(new Vector3(0, 0, 0.5f), Vector3.one * 0.12f, B, dish);
        }

        public static void SurveyProbe(ModelBuilder mb, BuildingDef def)
        {
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                mb.Box(dir * 0.5f + Vector3.up * 1f, new Vector3(0.08f, 2.1f, 0.08f), M, 0f, null, new Vector3(25f, a, 0));
            }
            mb.Frustum(new Vector3(0, 0.6f, 0), 0.04f, 0.2f, 1.6f, Palette.Metal, null, null, 8);
            mb.Box(new Vector3(0, 2.0f, 0), new Vector3(0.6f, 0.4f, 0.6f), L, 0.08f);
            mb.Strip(new Vector3(0, 2.0f, 0.31f), new Vector3(0.4f, 0.06f, 0.03f), O);
            mb.Cyl(new Vector3(0.2f, 2.8f, 0), 0.03f, 1.2f, Palette.Metal, null, null, 6);
        }

        public static void ShieldProjector(ModelBuilder mb, BuildingDef def)
        {
            mb.Box(new Vector3(0, 0.3f, 0), new Vector3(3.2f, 0.6f, 3.2f), M, 0.15f);
            mb.Frustum(new Vector3(0, 1.5f, 0), 0.9f, 0.4f, 1.8f, L, null, null, 16);
            for (int i = 0; i < 3; i++) mb.Torus(new Vector3(0, 1.0f + i * 0.5f, 0), 0.75f - i * 0.12f, 0.05f, B);
            mb.Sphere(new Vector3(0, 2.7f, 0), Vector3.one * 0.7f, B);
            var dome = mb.Group("#dome", new Vector3(0, 0f, 0));
            var domeMat = MaterialFactory.Cached("shield_dome", () => MaterialFactory.Transparent(new Color(0.3f, 0.7f, 1f, 0.09f), 0.95f, new Color(0.1f, 0.3f, 0.5f)));
            var t = mb.Sphere(Vector3.zero, Vector3.one, domeMat, dome);
            t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dome.gameObject.SetActive(false);
        }

        public static void DecoyBeacon(ModelBuilder mb, BuildingDef def)
        {
            mb.Hex(new Vector3(0, 0.15f, 0), 0.6f, 0.3f, D);
            mb.Cyl(new Vector3(0, 1.1f, 0), 0.1f, 1.8f, Palette.Metal, null, null, 8);
            mb.Frustum(new Vector3(0, 2.0f, 0.15f), 0.12f, 0.45f, 0.5f, L, null, new Vector3(80, 0, 0), 12);
            mb.Box(new Vector3(0, 0.6f, 0), new Vector3(0.45f, 0.5f, 0.35f), Palette.Hazard, 0.04f);
            var lamp = mb.Group("#lamp", new Vector3(0, 2.45f, 0));
            mb.Sphere(Vector3.zero, Vector3.one * 0.18f, O, lamp);
        }

        public static void LandingPad(ModelBuilder mb, BuildingDef def)
        {
            mb.Hex(new Vector3(0, 0.2f, 0), 4.1f, 0.4f, Palette.Concrete);
            mb.Hex(new Vector3(0, 0.25f, 0), 3.5f, 0.42f, D);
            mb.Torus(new Vector3(0, 0.47f, 0), 2.5f, 0.06f, O);
            mb.Strip(new Vector3(-0.8f, 0.47f, 0), new Vector3(0.25f, 0.03f, 2.2f), Palette.Hazard);
            mb.Strip(new Vector3(0.8f, 0.47f, 0), new Vector3(0.25f, 0.03f, 2.2f), Palette.Hazard);
            mb.Strip(new Vector3(0, 0.47f, 0), new Vector3(1.6f, 0.03f, 0.25f), Palette.Hazard);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                mb.Sphere(new Vector3(Mathf.Cos(a) * 3.7f, 0.5f, Mathf.Sin(a) * 3.7f), Vector3.one * 0.2f, B);
            }
            mb.PointLight(new Vector3(0, 3f, 0), new Color(0.5f, 0.8f, 1f), 10f, 1f);
        }

        public static void Spaceport(ModelBuilder mb, BuildingDef def)
        {
            mb.Hex(new Vector3(0, 0.25f, 0), 5f, 0.5f, Palette.Concrete);
            mb.Hex(new Vector3(0.6f, 0.3f, 0.6f), 3.6f, 0.52f, D);
            mb.Torus(new Vector3(0.6f, 0.57f, 0.6f), 2.8f, 0.08f, O);
            mb.Box(new Vector3(-3.6f, 3.2f, -3.6f), new Vector3(1.8f, 6.4f, 1.8f), L, 0.25f);
            mb.Box(new Vector3(-3.6f, 5.4f, -3.6f), new Vector3(1.85f, 0.6f, 1.85f), Palette.Glass, 0.05f);
            mb.Strip(new Vector3(-3.6f, 4.8f, -2.68f), new Vector3(1.2f, 0.08f, 0.04f), O);
            mb.Box(new Vector3(-1.9f, 5.0f, -3.6f), new Vector3(2.4f, 0.35f, 0.5f), M, 0.05f);
            mb.Cyl(new Vector3(3.6f, 1.6f, -3.4f), 0.8f, 2.6f, L, null, null, 16);
            mb.Cyl(new Vector3(3.6f, 1.6f, -1.6f), 0.8f, 2.6f, L, null, null, 16);
            mb.Strip(new Vector3(3.6f, 2.3f, -2.5f), new Vector3(0.05f, 0.08f, 2.6f), O);
            mb.Cyl(new Vector3(-3.6f, 8f, -3.6f), 0.06f, 3.2f, Palette.Metal, null, null, 6);
            mb.PointLight(new Vector3(0.6f, 4f, 0.6f), new Color(1f, 0.7f, 0.45f), 14f, 1.3f);
        }
    }
}
