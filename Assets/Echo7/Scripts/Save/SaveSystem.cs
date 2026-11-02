using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Echo7
{
    [Serializable] public class ResEntry { public int type; public float amount; }
    [Serializable] public class SkillEntry { public int id; public int level; }
    [Serializable] public class StructEntry
    {
        public int kind, x, y, w, h, rot;
        public float hp, progress, wear;
        public bool built, cableCut, mineArmed = true, freeOfCharge;
        public int crew = -1, dronePriority = -1;
        public float gateMechanism = 100f;
    }
    [Serializable] public class NpcEntry { public string name; public int role; public float x, z, hp, morale, personality, skill, sickness; public bool away; }
    [Serializable] public class DepositEntry { public float x, z; public int type, amount; public bool hidden, rich; }

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public string savedAt;
        public int seed;
        public int day;
        public float hour;
        public int rank, rankPoints, reputation;
        public int skillPoints;
        public List<SkillEntry> skills = new List<SkillEntry>();
        public List<ResEntry> resources = new List<ResEntry>();
        public List<StructEntry> structures = new List<StructEntry>();
        public List<NpcEntry> npcs = new List<NpcEntry>();
        public List<DepositEntry> deposits = new List<DepositEntry>();
        public List<int> openedCaves = new List<int>();
        public List<CenterTask> tasks = new List<CenterTask>();
        public int tasksCompleted, tasksFailed, tutorialStep;
        public List<string> flags = new List<string>();
        public int weather; public float weatherHours, greenness = 1f, water = 1f; public int weatherNextDay;
        public float nextDelivery; public int supplyPriority, supplySkip;
        public float playerX, playerZ, playerHp;
        public float noise, pressure;
        public int vipNextDay;
        public bool mineEstablished;
        public float reactorBonus;
    }

    /// <summary>
    /// Сохранение в JSON (persistentDataPath/echo7_save.json). Мир воссоздаётся из зерна, поверх кладутся
    /// постройки, залежи, гарнизон, прогрессия, погода и время. Волны в полёте не сохраняются.
    /// </summary>
    public class SaveSystem
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "echo7_save.json");
        public bool HasSave => File.Exists(FilePath);

        public static string ReadFile()
        {
            try { return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null; }
            catch (Exception e) { Debug.LogWarning("Эхо-7: не удалось прочитать сохранение: " + e.Message); return null; }
        }

        public static SaveData Parse(string json)
        {
            try { return JsonUtility.FromJson<SaveData>(json); }
            catch (Exception e) { Debug.LogWarning("Эхо-7: повреждённое сохранение: " + e.Message); return null; }
        }

        public void SaveGame()
        {
            if (Game.State != GameState.Playing) return;
            var d = Capture();
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(d, true));
                Game.Log?.Info($"Игра сохранена (день {d.day}).");
                Game.Sound?.Play2D(Sfx.Notify, 0.5f);
            }
            catch (Exception e) { Game.Log?.Alert("Ошибка сохранения: " + e.Message); }
        }

        static SaveData Capture()
        {
            var d = new SaveData
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                seed = Game.Config.seed,
                day = Game.Clock.Day,
                hour = Game.Clock.Hour,
                rank = (int)Game.Rank.Current,
                rankPoints = Game.Rank.Points,
                reputation = Game.Rank.Reputation,
                skillPoints = Game.Skills.Points,
                tasksCompleted = Game.Tasks.CompletedCount,
                tasksFailed = Game.Tasks.FailedCount,
                tutorialStep = Game.Tasks.TutorialStep,
                weather = (int)Game.Weather.Current,
                weatherHours = Game.Weather.HoursLeft,
                greenness = Game.Weather.Greenness,
                water = Game.Weather.WaterLevel,
                weatherNextDay = Game.Weather.NextEventDay,
                nextDelivery = Game.Supply.NextDeliveryHour,
                supplyPriority = (int)Game.Supply.Priority,
                supplySkip = Game.Supply.SkipNext,
                playerX = Game.Player.transform.position.x,
                playerZ = Game.Player.transform.position.z,
                playerHp = Game.Player.Health.Current,
                noise = Game.Noise.Level,
                pressure = Game.Onslaught.Pressure,
                vipNextDay = Game.Vip.NextVisitDay,
                mineEstablished = Game.Map.Get("mine")?.Established ?? false,
                reactorBonus = Game.Reactor != null ? Game.Reactor.BonusOutput : 0f,
            };
            foreach (var kv in Game.Skills.Snapshot()) d.skills.Add(new SkillEntry { id = (int)kv.Key, level = kv.Value });
            foreach (var t in ResourceBank.All) d.resources.Add(new ResEntry { type = (int)t, amount = Game.Bank.Exact(t) });
            d.tasks.AddRange(Game.Tasks.Active);
            d.flags.AddRange(Game.Entry.Flags);

            var npcs = new List<GarrisonNpc>();
            foreach (var n in Registry.Npcs) if (n != null && !n.Health.IsDead) npcs.Add(n);
            // NPC «на задании» неактивны, но в реестре остаются
            foreach (var n in npcs)
                d.npcs.Add(new NpcEntry { name = n.Name, role = (int)n.Role, x = n.transform.position.x, z = n.transform.position.z, hp = n.Health.Current, morale = n.Morale, personality = n.Personality, skill = n.Skill, sickness = n.Sickness, away = n.IsAway });

            foreach (var s in Registry.Structures)
            {
                if (s == null || s.IsDestroyed) continue;
                var e = new StructEntry
                {
                    kind = (int)s.Def.Kind, x = s.Footprint.x, y = s.Footprint.y, w = s.Footprint.width, h = s.Footprint.height, rot = s.Rotation,
                    hp = s.Health.Current, built = s.IsBuilt, progress = s.BuildProgress, cableCut = s.CableCut, freeOfCharge = s.FreeOfCharge
                };
                if (s is Turret t) { e.wear = t.Wear; if (t.Crew != null) e.crew = npcs.IndexOf(t.Crew); }
                if (s is Mine m) e.mineArmed = m.Armed;
                if (s is Gate g) e.gateMechanism = g.MechanismHp;
                if (s is DroneBay db) e.dronePriority = db.Priority.HasValue ? (int)db.Priority.Value : -1;
                d.structures.Add(e);
            }
            foreach (var dep in Registry.Deposits)
            {
                if (dep == null || dep.IsDepleted) continue;
                d.deposits.Add(new DepositEntry { x = dep.transform.position.x, z = dep.transform.position.z, type = (int)dep.Type, amount = dep.Amount, hidden = dep.Hidden, rich = dep.Rich });
            }
            for (int i = 0; i < Game.World.Caves.Count; i++) if (Game.World.Caves[i] != null && Game.World.Caves[i].Opened) d.openedCaves.Add(i);
            return d;
        }

        /// <summary>Шаг 1 загрузки: залежи, пещеры и постройки (до создания задач и снабжения).</summary>
        public static void ApplyWorld(SaveData d)
        {
            // залежи — заменить сгенерированные сохранёнными
            foreach (var dep in Registry.Deposits.ToArray())
                if (dep != null) { var c = Game.Grid.WorldToCell(dep.transform.position); if (Game.Grid.InBounds(c)) Game.Grid.Blocked[Game.Grid.Index(c.x, c.y)] = 0; UnityEngine.Object.Destroy(dep.gameObject); }
            Registry.Deposits.Clear();
            var depRoot = Game.World.transform;
            foreach (var e in d.deposits)
            {
                var p = new Vector3(e.x, 0, e.z);
                p.y = Game.Grid.SampleHeight(p);
                ResourceDeposit.Create(depRoot, p, (ResourceType)e.type, e.amount, e.hidden, e.rich);
            }
            foreach (int i in d.openedCaves) if (i >= 0 && i < Game.World.Caves.Count) Game.World.Caves[i].OpenSilently();

            foreach (var t in ResourceBank.All) Game.Bank.Set(t, 0);
            foreach (var r in d.resources) Game.Bank.Set((ResourceType)r.type, r.amount);

            foreach (var e in d.structures)
            {
                var def = BuildingCatalog.Get((StructureKind)e.kind);
                var size = BuildSystem.SizeRotated(def, e.rot);
                var center = new Vector2Int(e.x + size.x / 2, e.y + size.y / 2);
                var s = Game.Build.PlaceInstant(def.Kind, center, e.rot, e.built);
                if (s == null) continue;
                if (!e.built) s.AddProgress(e.progress);
                s.Health.Current = Mathf.Clamp(e.hp, 1f, s.Health.Max);
                s.FreeOfCharge = e.freeOfCharge;
                if (e.cableCut) s.CableCut = true;
                if (s is Turret t) t.Wear = e.wear;
                if (s is Mine m && !e.mineArmed) m.Disarm();
                if (s is Gate g) g.MechanismHp = e.gateMechanism;
                if (s is DroneBay db && e.dronePriority >= 0) db.Priority = (ResourceType)e.dronePriority;
                if (s is Reactor rc) rc.BonusOutput = d.reactorBonus;
            }
            Game.Power.MarkDirty();
        }

        /// <summary>Шаг 2 загрузки: прогрессия, гарнизон, время, погода, задачи.</summary>
        public static void ApplyState(SaveData d)
        {
            Game.Rank.Restore((Rank)d.rank, d.rankPoints, d.reputation);
            var levels = new Dictionary<SkillId, int>();
            foreach (var s in d.skills) levels[(SkillId)s.id] = s.level;
            Game.Skills.Restore(d.skillPoints, levels);
            foreach (var f in d.flags) Game.Entry.SetFlag(f);
            Game.Clock.SetTime(d.day, d.hour);
            Game.Weather.Restore((WeatherKind)d.weather, d.weatherHours, d.greenness, d.water, d.weatherNextDay);
            Game.Noise.Restore(d.noise);
            Game.Onslaught.RestorePressure(d.pressure);
            Game.Vip.Restore(d.vipNextDay);
            if (d.mineEstablished) { var m = Game.Map.Get("mine"); if (m != null) m.Established = true; }

            var spawned = new List<GarrisonNpc>();
            foreach (var e in d.npcs)
            {
                var n = Game.Garrison.Spawn((NpcRole)e.role, new Vector3(e.x, 0, e.z), e.name);
                n.Health.Current = Mathf.Clamp(e.hp, 1f, n.Health.Max);
                n.Morale = e.morale; n.Personality = e.personality; n.Skill = e.skill; n.Sickness = e.sickness;
                if (e.away) n.SendAway();
                spawned.Add(n);
            }
            foreach (var e in d.structures)
            {
                if (e.crew < 0 || e.crew >= spawned.Count) continue;
                var s = Game.Grid.StructureAt(e.x, e.y) as Turret;
                if (s != null) Game.Defense.AssignCrew(s, spawned[e.crew]);
            }

            Game.Tasks.Restore(d.tasks, d.tasksCompleted, d.tasksFailed, d.tutorialStep);
            Game.Tasks.InitEventsOnly();
            Game.Supply.Init();
            Game.Supply.Restore(d.nextDelivery, (SupplyPriority)d.supplyPriority, d.supplySkip);
            Game.Player.ApplySkillBonuses();
            Game.Player.Teleport(new Vector3(d.playerX, 0, d.playerZ));
            Game.Player.Health.Current = Mathf.Clamp(d.playerHp, 1f, Game.Player.Health.Max);
        }
    }
}
