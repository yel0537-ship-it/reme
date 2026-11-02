using UnityEngine;

namespace Echo7
{
    /// <summary>Объект, с которым игрок взаимодействует (клик/E).</summary>
    public interface IInteractable
    {
        string InteractLabel { get; }
        Vector3 InteractPoint { get; }
        float InteractRange { get; }
        bool CanInteract { get; }
        void Interact(PlayerController player);
    }

    /// <summary>
    /// Залежь ресурса (титан, кристаллы, кремний, металлолом). Добывается вручную игроком,
    /// дронами или экстрактором. Добыча шумит — это провоцирует волны.
    /// </summary>
    public class ResourceDeposit : MonoBehaviour, IInteractable
    {
        public ResourceType Type;
        public int Amount;
        public int Initial;
        public bool Hidden;
        public bool Rich;
        public Extractor BoundExtractor;
        public int ReservedByDrones;
        Transform _visual;

        public bool IsDepleted => Amount <= 0;
        public string InteractLabel => $"Добыть: {ResourceBank.Name(Type)} ({Amount})";
        public Vector3 InteractPoint => transform.position;
        public float InteractRange => 2.8f;
        public bool CanInteract => !Hidden && !IsDepleted && BoundExtractor == null;

        public static ResourceDeposit Create(Transform parent, Vector3 pos, ResourceType type, int amount, bool hidden, bool rich = false)
        {
            var go = new GameObject("Deposit_" + type);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.layer = Layers.Deposit;
            var d = go.AddComponent<ResourceDeposit>();
            d.Type = type; d.Amount = amount; d.Initial = amount; d.Hidden = hidden; d.Rich = rich;
            d.BuildVisual();
            var col = go.AddComponent<SphereCollider>();
            col.radius = 1.4f;
            col.center = Vector3.up * 0.6f;
            Registry.Deposits.Add(d);
            if (Game.Grid != null)
            {
                var c = Game.Grid.WorldToCell(pos);
                if (Game.Grid.InBounds(c)) Game.Grid.Blocked[Game.Grid.Index(c.x, c.y)] = 2;
            }
            d.SetHidden(hidden);
            return d;
        }

        void BuildVisual()
        {
            var mb = new ModelBuilder(transform, "#visual");
            _visual = mb.Root;
            var rnd = new System.Random(GetInstanceID());
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            switch (Type)
            {
                case ResourceType.Titanium:
                {
                    var ore = MaterialFactory.Cached("ore_ti", () => MaterialFactory.Lit(new Color(0.32f, 0.34f, 0.38f), 0.75f, 0.55f));
                    for (int i = 0; i < 5; i++)
                    {
                        float s = R(0.6f, 1.3f);
                        var t = mb.Mesh(MeshFactory.Boulder(rnd.Next(6)), i == 0 ? ore : Palette.RockDark,
                            new Vector3(R(-1f, 1f), s * 0.3f, R(-1f, 1f)), new Vector3(s, s * 0.8f, s), null, new Vector3(0, R(0, 360), 0));
                    }
                    var vein = MaterialFactory.Cached("ore_ti_vein", () => MaterialFactory.Emissive(new Color(0.5f, 0.7f, 0.9f), new Color(0.3f, 0.55f, 0.9f), 1.2f));
                    for (int i = 0; i < 4; i++)
                        mb.Strip(new Vector3(R(-0.8f, 0.8f), R(0.3f, 0.8f), R(-0.8f, 0.8f)), new Vector3(0.06f, 0.06f, R(0.4f, 0.9f)), vein, null, new Vector3(R(-30, 30), R(0, 360), 0));
                    break;
                }
                case ResourceType.Crystals:
                {
                    var cr = MaterialFactory.Cached("crystal", () => MaterialFactory.Emissive(new Color(0.55f, 0.3f, 0.9f), new Color(0.5f, 0.25f, 1f), 1.6f));
                    mb.Mesh(MeshFactory.Boulder(2), Palette.RockDark, new Vector3(0, 0.2f, 0), new Vector3(1.6f, 0.6f, 1.4f));
                    for (int i = 0; i < 7; i++)
                    {
                        float s = R(0.7f, 1.8f);
                        mb.Mesh(MeshFactory.CrystalShard(), cr, new Vector3(R(-0.6f, 0.6f), 0.2f, R(-0.6f, 0.6f)), new Vector3(s, s * R(1f, 1.6f), s), null,
                            new Vector3(R(-30, 30), R(0, 360), R(-30, 30)));
                    }
                    break;
                }
                case ResourceType.Silicon:
                {
                    var sand = MaterialFactory.Cached("silicon", () => MaterialFactory.Lit(new Color(0.88f, 0.84f, 0.70f), 0.1f, 0.6f));
                    var flake = MaterialFactory.Cached("silicon_flake", () => MaterialFactory.Lit(new Color(0.95f, 0.95f, 1f), 0.6f, 0.95f));
                    mb.Sphere(new Vector3(0, 0f, 0), new Vector3(2.6f, 0.9f, 2.2f), sand);
                    for (int i = 0; i < 9; i++)
                        mb.Box(new Vector3(R(-0.9f, 0.9f), R(0.25f, 0.45f), R(-0.8f, 0.8f)), new Vector3(0.25f, 0.05f, 0.18f), flake, 0f, null, new Vector3(R(0, 40), R(0, 360), R(0, 40)));
                    break;
                }
                default:
                {
                    // обломки старой экспедиции — источник сплавов
                    for (int i = 0; i < 6; i++)
                        mb.Box(new Vector3(R(-1.2f, 1.2f), R(0.1f, 0.5f), R(-1.2f, 1.2f)), new Vector3(R(0.4f, 1.4f), R(0.2f, 0.6f), R(0.4f, 1.2f)),
                            i % 2 == 0 ? Palette.HullMid : Palette.HullDark, 0.05f, null, new Vector3(R(-25, 25), R(0, 360), R(-25, 25)));
                    mb.Strip(new Vector3(0.3f, 0.6f, 0.1f), new Vector3(0.5f, 0.05f, 0.05f), Palette.Orange, null, new Vector3(0, 30, 15));
                    break;
                }
            }
            if (Rich) _visual.localScale = Vector3.one * 1.35f;
            ModelBuilder.CombineStatic(_visual);
        }

        public void SetHidden(bool hidden)
        {
            Hidden = hidden;
            if (_visual != null) _visual.gameObject.SetActive(!hidden);
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = !hidden;
        }

        /// <summary>Забрать до amount единиц; возвращает добытое.</summary>
        public int Take(int amount)
        {
            int got = Mathf.Min(amount, Amount);
            Amount -= got;
            if (got > 0) GameEvents.Mined(this, got);
            if (_visual != null) _visual.localScale = Vector3.one * (Rich ? 1.35f : 1f) * Mathf.Lerp(0.45f, 1f, Initial > 0 ? (float)Amount / Initial : 0f);
            if (IsDepleted)
            {
                if (Game.Grid != null)
                {
                    var c = Game.Grid.WorldToCell(transform.position);
                    if (Game.Grid.InBounds(c) && Game.Grid.Blocked[Game.Grid.Index(c.x, c.y)] == 2) Game.Grid.Blocked[Game.Grid.Index(c.x, c.y)] = 0;
                }
                Game.Log?.Info($"Залежь «{ResourceBank.Name(Type)}» истощена.");
                Registry.Deposits.Remove(this);
                Destroy(gameObject, 0.1f);
            }
            return got;
        }

        public void Interact(PlayerController player) => player.StartMining(this);
    }

    /// <summary>
    /// Вход в пещеру, заваленный камнями. Подрывной заряд открывает богатые жилы, но взрыв
    /// поднимает огромный шум — почти гарантированная волна.
    /// </summary>
    public class CaveEntrance : MonoBehaviour, IInteractable
    {
        public bool Opened;
        public int Index;
        Transform _rubble;

        public string InteractLabel => Opened ? "Пещера (жилы снаружи)" : "Завал: заложить подрывной заряд [4]";
        public Vector3 InteractPoint => transform.position + transform.forward * 3f;
        public float InteractRange => 4f;
        public bool CanInteract => !Opened;

        public static CaveEntrance Create(Transform parent, Vector3 pos, Quaternion facing, int index)
        {
            var go = new GameObject("Cave_" + index);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = facing;
            go.layer = Layers.Interactable;
            var c = go.AddComponent<CaveEntrance>();
            c.Index = index;
            c.Build();
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 2f, 1.5f);
            col.size = new Vector3(6f, 4f, 3f);
            return c;
        }

        void Build()
        {
            var mb = new ModelBuilder(transform, "Arch");
            var rnd = new System.Random(Index * 97 + 3);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            // скальная арка
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Lerp(-80f, 80f, i / 8f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a) * 3.6f, Mathf.Cos(a) * 3.4f, -1f);
                float s = R(1.8f, 2.6f);
                mb.Mesh(MeshFactory.Boulder(rnd.Next(6)), Palette.RockDark, p, new Vector3(s, s, s * 1.4f), null, new Vector3(R(0, 30), R(0, 360), R(0, 30)));
            }
            mb.Mesh(MeshFactory.Boulder(1), Palette.Rock, new Vector3(0, 4.5f, -2.5f), new Vector3(9f, 4f, 6f));
            // тёмный зев
            mb.Box(new Vector3(0, 1.6f, -1.2f), new Vector3(4.2f, 3.4f, 0.4f), MaterialFactory.Cached("cave_dark", () => MaterialFactory.Lit(new Color(0.02f, 0.02f, 0.025f), 0f, 0f)), 0f);
            ModelBuilder.CombineStatic(mb.Root);
            // завал
            var rb = new ModelBuilder(transform, "#Rubble");
            _rubble = rb.Root;
            for (int i = 0; i < 7; i++)
            {
                float s = R(0.9f, 1.6f);
                rb.Mesh(MeshFactory.Boulder(rnd.Next(6)), Palette.Rock, new Vector3(R(-1.6f, 1.6f), s * 0.4f + R(0f, 1.2f), R(-0.6f, 0.4f)), Vector3.one * s, null, new Vector3(0, R(0, 360), 0));
            }
        }

        public void Interact(PlayerController player)
        {
            if (Opened) return;
            player.TryPlaceCharge(this);
        }

        /// <summary>Открыть без взрыва и без новых залежей (загрузка сохранения).</summary>
        public void OpenSilently()
        {
            Opened = true;
            if (_rubble != null) Destroy(_rubble.gameObject);
            Game.Grid.SetBlocked(transform.position - transform.forward * 2f, 4.5f, false);
        }

        /// <summary>Подрыв: открывает пещеру и выкладывает богатые залежи у входа.</summary>
        public void Blast()
        {
            if (Opened) return;
            Opened = true;
            var pos = transform.position + transform.forward * 1.5f + Vector3.up;
            Game.Fx.Explosion(pos, 7f);
            Game.Sound.PlayAt(Sfx.Explosion, pos, 1.2f);
            Game.Noise.Emit(pos, 70f, NoiseSource.Explosion);
            if (_rubble != null) Destroy(_rubble.gameObject);
            Game.Grid.SetBlocked(transform.position - transform.forward * 2f, 4.5f, false);
            var parent = transform.parent;
            var rnd = new System.Random(Index * 13 + 1);
            for (int i = 0; i < 3; i++)
            {
                var type = i == 0 ? ResourceType.Crystals : i == 1 ? ResourceType.Titanium : (rnd.NextDouble() < 0.5 ? ResourceType.Crystals : ResourceType.Titanium);
                var p = transform.position + transform.right * (i - 1) * 3.2f + transform.forward * (2.5f + (float)rnd.NextDouble() * 2f);
                p.y = Game.Grid.SampleHeight(p);
                ResourceDeposit.Create(parent, p, type, 180 + rnd.Next(120), false, true);
            }
            Game.Log?.Alert("Завал подорван! Богатые жилы открыты, но грохот слышен на всю саванну.");
            GameEvents.Cave(this);
        }
    }
}
