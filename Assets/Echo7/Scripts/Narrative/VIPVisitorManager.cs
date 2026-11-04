using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum VipType { Inspector, General, Curator }

    /// <summary>Запрос высокого чина: что он хочет получить от аванпоста.</summary>
    public class VipRequest
    {
        public string Name;
        public string Description;
        public Cost BestCost;          // «лучшее»
        public Cost JunkCost;          // «хлам»
        public bool NeedsNpc;          // медик/охрана — NPC уходит с миссией
        public NpcRole NpcRole;
        public int Choice = -1;        // 0 — лучшее, 1 — хлам, 2 — отказ
    }

    public class VipVisit
    {
        public VipType Type;
        public string Name;
        public string Title;
        public string Mission;
        public readonly List<VipRequest> Requests = new List<VipRequest>();
        public readonly List<GarrisonNpc> LentNpcs = new List<GarrisonNpc>();
        public float Quality;          // 0..1 — качество поддержки
        public bool Refused;
        public float ResolveAtHour;
        public bool Resolved;
        public string RewardFlag;
    }

    /// <summary>
    /// Визиты высоких чинов. Челнок садится на площадку (или в зону высадки), инспектор/генерал/куратор
    /// просит лучшую броню, медиков, транспорт. Дать — репутация и уникальные награды; отказать или дать хлам —
    /// падение репутации, урезанные поставки; гибель чина — военный трибунал и понижение в ранге.
    /// </summary>
    public class VIPVisitorManager : MonoBehaviour
    {
        public VipVisit Current { get; private set; }
        public VipVisit Pending { get; private set; }   // улетел на миссию, ждём исход
        public VipNpc VipActor { get; private set; }
        public int NextVisitDay { get; private set; } = 3;
        Shuttle _shuttle;
        float _arrivedHour = -1f;
        readonly System.Random _rnd = new System.Random(77);
        int _visitCount;

        public void Init(int seed)
        {
            GameEvents.HourPassed += OnHour;
        }

        void OnDestroy() { GameEvents.HourPassed -= OnHour; }

        void OnHour(int hour)
        {
            if (!Game.Config.enableVipVisits) return;
            if (Game.Tutorial != null && !Game.Tutorial.AllowVip) { NextVisitDay = Mathf.Max(NextVisitDay, Game.Clock.Day + 2); return; }
            if (Pending != null && !Pending.Resolved && Game.Clock.TotalHours >= Pending.ResolveAtHour) Resolve(Pending);
            // гость не дождался встречи — улетает, считая это отказом
            if (Current != null && _arrivedHour >= 0f && Game.Clock.TotalHours - _arrivedHour > 20f)
            {
                Game.Log?.Center($"{Current.Title} {Current.Name} не дождался(ась) встречи с командиром и улетает.");
                foreach (var q in Current.Requests) if (q.Choice < 0) q.Choice = 2;
                Depart(Current);
            }
            if (Current == null && Pending == null && Game.Clock.Day >= NextVisitDay && hour == 10) BeginVisit();
        }

        public void ForceVisit() { if (Current == null) BeginVisit(); }

        void BeginVisit()
        {
            _visitCount++;
            var type = (VipType)(_visitCount % 3 == 1 ? 0 : _rnd.Next(3));
            Current = MakeVisit(type);
            _arrivedHour = Game.Clock.TotalHours;
            var pad = StructureLookup.FirstOf(StructureKind.LandingPad) as LandingPad;
            var at = pad != null && pad.IsBuilt ? pad.PadPoint : Game.World.LandingZone;
            _shuttle = Shuttle.Land(at, () =>
            {
                VipActor = VipNpc.Spawn(Current, at + Vector3.right * 3f);
                Game.Log?.Center($"На аванпост прибыл(а) {Current.Title} {Current.Name}. Поговорите с гостем.");
            });
            Game.Log?.Center($"Центр: к вам направляется {Current.Title.ToLower()} {Current.Name}. Встречайте.");
        }

        VipVisit MakeVisit(VipType type)
        {
            var v = new VipVisit { Type = type };
            int r = (int)Game.Rank.Current;
            switch (type)
            {
                case VipType.Inspector:
                    v.Title = "Инспектор снабжения"; v.Name = "Ворошилова";
                    v.Mission = "Мне нужно добраться до заброшенного склада экспедиции на северном плато. Там остались документы, которые не должны попасть к конкурентам.";
                    v.RewardFlag = "market_closed";
                    v.Requests.Add(new VipRequest { Name = "Лучшая броня", Description = "Комплект усиленной брони", BestCost = new Cost((ResourceType.Titanium, 15 + r * 5), (ResourceType.Alloys, 20)), JunkCost = new Cost((ResourceType.Alloys, 6)) });
                    v.Requests.Add(new VipRequest { Name = "Транспорт", Description = "Вездеход с запасом хода", BestCost = new Cost((ResourceType.Alloys, 35), (ResourceType.Silicon, 8), (ResourceType.Crystals, 4)), JunkCost = new Cost((ResourceType.Alloys, 10)) });
                    break;
                case VipType.General:
                    v.Title = "Генерал"; v.Name = "Арсеньев";
                    v.Mission = "Разведка донесла о гнезде «танков» в каньоне. Я лично поведу ударную группу. Аванпост обязан обеспечить операцию.";
                    v.RewardFlag = "bp_shield";
                    v.Requests.Add(new VipRequest { Name = "Боеприпасы", Description = "Полный боекомплект группы", BestCost = new Cost((ResourceType.Ammo, 120 + r * 30)), JunkCost = new Cost((ResourceType.Ammo, 30)) });
                    v.Requests.Add(new VipRequest { Name = "Медик", Description = "Лучший медик аванпоста уходит с группой", NeedsNpc = true, NpcRole = NpcRole.Medic, BestCost = new Cost((ResourceType.Medkits, 3)), JunkCost = new Cost((ResourceType.Medkits, 1)) });
                    v.Requests.Add(new VipRequest { Name = "Лучшая броня", Description = "Броня для штурмовиков", BestCost = new Cost((ResourceType.Titanium, 20 + r * 5)), JunkCost = new Cost((ResourceType.Alloys, 8)) });
                    break;
                default:
                    v.Title = "Научный куратор"; v.Name = "Делакруа";
                    v.Mission = "В пещерах найден артефакт неизвестной цивилизации. Его нужно доставить на орбитальную станцию в целости.";
                    v.RewardFlag = "artifact_research";
                    v.Requests.Add(new VipRequest { Name = "Охрана", Description = "Боец сопровождения", NeedsNpc = true, NpcRole = NpcRole.Soldier, BestCost = new Cost((ResourceType.Ammo, 40)), JunkCost = new Cost((ResourceType.Ammo, 10)) });
                    v.Requests.Add(new VipRequest { Name = "Транспорт", Description = "Защищённый контейнер и вездеход", BestCost = new Cost((ResourceType.Alloys, 30), (ResourceType.Crystals, 6)), JunkCost = new Cost((ResourceType.Alloys, 8)) });
                    break;
            }
            return v;
        }

        // ------------------------------------------------------------ диалог

        static readonly Color VipColor = new Color(0.55f, 0.45f, 0.15f);

        public DialogueNode Dialogue()
        {
            var v = Current;
            if (v == null) return null;
            var node = new DialogueNode
            {
                Speaker = $"{v.Title} {v.Name}",
                Portrait = v.Type == VipType.General ? "★" : v.Type == VipType.Inspector ? "И" : "К",
                PortraitColor = VipColor,
                Text = $"{v.Mission}\n\nМне нужно от вас:\n" + RequestList(v)
            };
            for (int i = 0; i < v.Requests.Count; i++)
            {
                var rq = v.Requests[i];
                if (rq.Choice >= 0) continue;
                int idx = i;
                node.Add($"{rq.Name}: выдать лучшее ({rq.BestCost}{(rq.NeedsNpc ? " + боец" : "")})", () => Give(v, idx, 0), Dialogue,
                    () => CanGive(rq, 0));
                node.Add($"{rq.Name}: выдать что похуже ({rq.JunkCost})", () => Give(v, idx, 1), Dialogue, () => CanGive(rq, 1), "Сэкономите, но рискуете");
                node.Add($"{rq.Name}: отказать", () => Give(v, idx, 2), Dialogue);
                break; // по одному запросу за раз
            }
            bool allDone = v.Requests.TrueForAll(q => q.Choice >= 0);
            if (allDone) node.Add("Удачи на задании. (Завершить)", () => Depart(v));
            else node.Add("Отказать во всём («У аванпоста нет лишних ресурсов»)", () => { foreach (var q in v.Requests) if (q.Choice < 0) q.Choice = 2; Depart(v); });
            node.Add("Отойти (вернуться к разговору позже)");
            return node;
        }

        static string RequestList(VipVisit v)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var q in v.Requests)
            {
                string st = q.Choice == 0 ? " — выдано лучшее" : q.Choice == 1 ? " — выдан хлам" : q.Choice == 2 ? " — отказано" : "";
                sb.Append("• ").Append(q.Name).Append(": ").Append(q.Description).Append(st).Append('\n');
            }
            return sb.ToString();
        }

        string CanGive(VipRequest rq, int quality)
        {
            var cost = quality == 0 ? rq.BestCost : rq.JunkCost;
            if (!Game.Bank.CanAfford(cost)) return Game.Bank.MissingText(cost);
            if (rq.NeedsNpc && quality == 0 && FindNpc(rq.NpcRole) == null) return $"Нет свободного бойца ({(rq.NpcRole == NpcRole.Medic ? "медик" : "солдат")})";
            return null;
        }

        GarrisonNpc FindNpc(NpcRole role)
        {
            GarrisonNpc best = null;
            foreach (var n in Registry.Npcs)
            {
                if (n == null || n.Health.IsDead || n.IsAway) continue;
                if (n.Role == role && (best == null || n.Skill > best.Skill)) best = n;
            }
            if (best == null && role == NpcRole.Soldier)
                foreach (var n in Registry.Npcs) if (n != null && !n.Health.IsDead && !n.IsAway) { best = n; break; }
            return best;
        }

        void Give(VipVisit v, int idx, int quality)
        {
            var rq = v.Requests[idx];
            rq.Choice = quality;
            if (quality == 2) return;
            Game.Bank.Spend(quality == 0 ? rq.BestCost : rq.JunkCost);
            if (rq.NeedsNpc && quality == 0)
            {
                var npc = FindNpc(rq.NpcRole);
                if (npc != null) { v.LentNpcs.Add(npc); npc.SendAway(); Game.Log?.Info($"{npc.DisplayName} отправляется с миссией."); }
            }
        }

        void Depart(VipVisit v)
        {
            float q = 0f;
            int refused = 0;
            foreach (var r in v.Requests)
            {
                q += r.Choice == 0 ? 1f : r.Choice == 1 ? 0.3f : 0f;
                if (r.Choice == 2) refused++;
            }
            v.Quality = v.Requests.Count > 0 ? q / v.Requests.Count : 0f;
            v.Refused = refused == v.Requests.Count;

            if (v.Refused)
            {
                Game.Rank.AddReputation(-14, $"отказ {v.Title.ToLower()}у");
                Game.Supply.SkipNext++;
                Game.Log?.Center($"{v.Title} {v.Name} улетает ни с чем. «Я доложу о вашем отношении, офицер».");
            }
            else if (v.Quality < 0.5f)
            {
                Game.Rank.AddReputation(-5, "скупость при обеспечении миссии");
                Game.Log?.Center($"{v.Title} {v.Name} с недовольным видом забирает выданное.");
            }
            else
            {
                Game.Rank.AddReputation(4, "обеспечение миссии");
                Game.Log?.Center($"{v.Title} {v.Name}: «Отличная работа, аванпост. Я этого не забуду».");
            }
            v.ResolveAtHour = Game.Clock.TotalHours + 24f + _rnd.Next(12);
            Pending = v;
            Current = null;
            _arrivedHour = -1f;
            if (VipActor == null && _shuttle != null) { _shuttle.TakeOff(); _shuttle = null; }
            if (VipActor != null) VipActor.WalkTo(_shuttle != null ? _shuttle.transform.position : Game.World.LandingZone, () =>
            {
                if (VipActor != null) Destroy(VipActor.gameObject);
                VipActor = null;
                if (_shuttle != null) _shuttle.TakeOff();
                _shuttle = null;
            });
            NextVisitDay = Game.Clock.Day + 3 + _rnd.Next(3);
        }

        /// <summary>Исход миссии высокого чина.</summary>
        void Resolve(VipVisit v)
        {
            v.Resolved = true;
            Pending = null;
            float success = 0.3f + 0.65f * v.Quality;
            bool ok = _rnd.NextDouble() < success;
            var returnAt = Game.World.LandingZone;
            if (ok)
            {
                int rep = Mathf.RoundToInt(6 + 14 * v.Quality);
                Game.Rank.AddReputation(rep, $"успех миссии: {v.Title.ToLower()} {v.Name}");
                Game.Rank.AddPoints(Mathf.RoundToInt(30 + 50 * v.Quality), "благодарность высокого чина");
                if (v.Quality >= 0.7f && !string.IsNullOrEmpty(v.RewardFlag))
                {
                    Game.Entry.SetFlag(v.RewardFlag);
                    switch (v.RewardFlag)
                    {
                        case "bp_shield": Game.Log?.Center("Награда: уникальный чертёж «Генератор щита» (меню строительства → Оборона)."); break;
                        case "market_closed": Game.Log?.Center("Награда: доступ к закрытому рынку орбитальной станции."); break;
                        case "artifact_research": Game.Bank.Add(ResourceType.Credits, 150, true); Game.Log?.Center("Награда: исследовательский грант — 150 кредитов."); break;
                    }
                }
                foreach (var n in v.LentNpcs) if (n != null) { n.ReturnFromAway(returnAt); n.AddMorale(15f); Game.Log?.Info($"{n.DisplayName} вернулся(ась) с миссии."); }
                Game.Log?.Center($"Миссия «{v.Title} {v.Name}» успешна.");
            }
            else
            {
                Game.Log?.Alert($"Центр: {v.Title.ToLower()} {v.Name} погиб(ла) при выполнении миссии.");
                foreach (var n in v.LentNpcs)
                {
                    if (n == null) continue;
                    if (_rnd.NextDouble() < 0.5) { n.ReturnFromAway(returnAt); n.AddMorale(-20f); Game.Log?.Warn($"{n.DisplayName} вернулся(ась) — единственный(ая) выживший(ая)."); }
                    else { n.ReturnFromAway(returnAt); n.Health.TakeDamage(new DamageInfo(9999f, DamageType.Melee, n.transform.position, Faction.Hostile)); }
                }
                if (v.Quality < 0.5f || v.Refused)
                {
                    Game.Rank.Demote($"гибель {v.Title.ToLower()}а {v.Name} при недостаточном обеспечении миссии");
                    Game.Rank.AddReputation(-20, "трибунал");
                }
                else Game.Rank.AddReputation(-4, "гибель высокого чина (обеспечение было достаточным)");
            }
        }

        public void Restore(int nextDay) { NextVisitDay = nextDay; }
    }

    /// <summary>NPC высокого чина на базе (декоративный, с диалогом).</summary>
    public class VipNpc : MonoBehaviour, IInteractable
    {
        VipVisit _visit;
        HumanoidRig _rig;
        Vector3? _walk;
        System.Action _onArrive;

        public string InteractLabel => _visit != null ? $"Поговорить: {_visit.Title} {_visit.Name}" : "";
        public Vector3 InteractPoint => transform.position;
        public float InteractRange => 3f;
        public bool CanInteract => _visit != null && Game.Vip.Current == _visit;

        public static VipNpc Spawn(VipVisit v, Vector3 pos)
        {
            var go = new GameObject("VIP");
            go.transform.SetParent(Game.Root, false);
            pos.y = Game.Grid.SampleHeight(pos);
            go.transform.position = pos;
            go.layer = Layers.Npc;
            var npc = go.AddComponent<VipNpc>();
            npc._visit = v;
            var st = HumanoidStyle.Soldier;
            st.Plates = MaterialFactory.Cached("vip_plates", () => MaterialFactory.Lit(new Color(0.12f, 0.15f, 0.25f), 0.6f, 0.6f));
            st.Accent = MaterialFactory.Cached("vip_gold", () => MaterialFactory.Emissive(new Color(0.9f, 0.7f, 0.25f), new Color(0.5f, 0.35f, 0.1f), 1f));
            st.Visor = Palette.Orange;
            st.Cape = true;
            st.Backpack = false;
            st.Scale = 1.05f;
            npc._rig = HumanoidRig.Build(go.transform, st);
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.4f; col.height = 1.9f; col.center = new Vector3(0, 0.95f, 0); col.isTrigger = true;
            // идёт к командному модулю
            var cmd = StructureLookup.FirstOf(StructureKind.Command);
            if (cmd != null) npc.WalkTo(cmd.ClosestPoint(pos) + (pos - cmd.transform.position).normalized * 1.5f, null);
            return npc;
        }

        public void WalkTo(Vector3 p, System.Action onArrive)
        {
            _walk = p;
            _onArrive = onArrive;
        }

        void Update()
        {
            if (!Game.IsPlaying) return;
            if (_walk.HasValue)
            {
                var to = _walk.Value - transform.position; to.y = 0;
                if (to.magnitude < 0.6f)
                {
                    _walk = null;
                    _rig.Speed = 0f;
                    var cb = _onArrive; _onArrive = null;
                    cb?.Invoke();
                    return;
                }
                var step = to.normalized * 3f * Time.deltaTime;
                var p = transform.position + step;
                p.y = Game.Grid.SampleHeight(p);
                transform.position = p;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 6f);
                _rig.Speed = 3f;
            }
            else _rig.Speed = 0f;
        }

        public void Interact(PlayerController player)
        {
            var d = Game.Vip.Dialogue();
            if (d != null) Game.Dialogue.Start(d);
        }
    }

    /// <summary>Челнок: заходит на посадку, ждёт, улетает.</summary>
    public class Shuttle : MonoBehaviour
    {
        Vector3 _pad;
        float _t;
        int _phase; // 0 — посадка, 1 — стоит, 2 — взлёт
        System.Action _onLanded;
        ParticleSystem _jet;

        public static Shuttle Land(Vector3 pad, System.Action onLanded)
        {
            var go = new GameObject("Shuttle");
            go.transform.SetParent(Game.Root, false);
            var s = go.AddComponent<Shuttle>();
            s._pad = pad;
            s._onLanded = onLanded;
            var mb = new ModelBuilder(go.transform, "m");
            mb.Box(new Vector3(0, 1.4f, 0), new Vector3(3.2f, 2f, 7f), Palette.HullLight, 0.5f);
            mb.Box(new Vector3(0, 1.6f, 3.6f), new Vector3(2.4f, 1.4f, 1.6f), Palette.HullLight, 0.5f);
            mb.Box(new Vector3(0, 2f, 4.2f), new Vector3(2f, 0.6f, 0.8f), Palette.Glass, 0.2f);
            mb.Box(new Vector3(0, 1.2f, -0.5f), new Vector3(9f, 0.25f, 2.6f), Palette.HullMid, 0.1f);
            mb.Box(new Vector3(0, 2.8f, -2.6f), new Vector3(0.3f, 1.8f, 1.6f), Palette.HullMid, 0.1f);
            mb.Strip(new Vector3(0, 1.0f, 3.52f), new Vector3(2.2f, 0.1f, 0.05f), Palette.Orange);
            mb.Strip(new Vector3(4.4f, 1.2f, -0.5f), new Vector3(0.1f, 0.1f, 2.4f), Palette.Red);
            mb.Strip(new Vector3(-4.4f, 1.2f, -0.5f), new Vector3(0.1f, 0.1f, 2.4f), Palette.Blue);
            for (int i = -1; i <= 1; i += 2)
            {
                mb.Cyl(new Vector3(i * 1.0f, 1.2f, -3.7f), 0.5f, 0.8f, Palette.HullDark, null, new Vector3(90, 0, 0), 12);
                mb.Cyl(new Vector3(i * 1.0f, 1.2f, -4.1f), 0.42f, 0.05f, Palette.Blue, null, new Vector3(90, 0, 0), 12);
                mb.Box(new Vector3(i * 1.2f, 0.25f, 0), new Vector3(0.15f, 0.5f, 3f), Palette.HullDark, 0.03f);
            }
            ModelBuilder.CombineStatic(mb.Root);
            go.transform.position = pad + new Vector3(0, 60f, -80f);
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            s._jet = Game.Fx.CreateLooping("jet", go.transform, new Color(0.5f, 0.8f, 1f, 0.7f), 60f, 0.5f, 6f, 0.8f, ParticleSystemShapeType.Cone, Vector3.one * 0.6f);
            s._jet.transform.localPosition = new Vector3(0, 0.3f, 0);
            s._jet.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            Game.Sound.PlayAt(Sfx.Shuttle, pad, 1f);
            return s;
        }

        public void TakeOff()
        {
            _phase = 2;
            _t = 0f;
            Game.Sound.PlayAt(Sfx.Shuttle, transform.position, 1f);
            var em = _jet.emission; em.rateOverTime = 60f;
        }

        void Update()
        {
            switch (_phase)
            {
                case 0:
                    _t += Time.deltaTime / 7f;
                    float k = 1f - (1f - Mathf.Clamp01(_t)) * (1f - Mathf.Clamp01(_t));
                    var start = _pad + new Vector3(0, 60f, -80f);
                    var hover = _pad + Vector3.up * 8f;
                    transform.position = _t < 0.7f ? Vector3.Lerp(start, hover, k / 0.91f) : Vector3.Lerp(hover, _pad, (_t - 0.7f) / 0.3f);
                    if (_t >= 1f)
                    {
                        transform.position = _pad;
                        _phase = 1;
                        var em = _jet.emission; em.rateOverTime = 0f;
                        Game.Fx.Emit(FxKind.Dust, _pad, 30, Vector3.up, 1f, null, 1.5f);
                        Game.Noise?.Emit(_pad, 12f, NoiseSource.Event);
                        _onLanded?.Invoke();
                    }
                    break;
                case 2:
                    _t += Time.deltaTime / 6f;
                    transform.position = _pad + Vector3.up * (_t * _t * 70f) + Vector3.forward * (_t * _t * 90f);
                    if (_t >= 1f) Destroy(gameObject);
                    break;
            }
        }
    }
}
