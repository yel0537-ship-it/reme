using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
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
        public string Portrait = "?";
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
        public string RewardMessage;
        public int RewardCredits;
    }

    /// <summary>
    /// Визиты высоких чинов. Челнок садится на площадку (или в зону высадки), инспектор/генерал/куратор
    /// просит лучшую броню, медиков, транспорт. Дать — репутация и уникальные награды; отказать или дать хлам —
    /// падение репутации, урезанные поставки; гибель чина — военный трибунал и понижение в ранге.
    /// Визиты, расписание и последствия — в ассете <see cref="VipBalance"/> (Resources/Echo7Balance/Vips).
    /// </summary>
    public class VIPVisitorManager : MonoBehaviour
    {
        public VipVisit Current { get; private set; }
        public VipVisit Pending { get; private set; }   // улетел на миссию, ждём исход
        public VipNpc VipActor { get; private set; }
        public int NextVisitDay { get; private set; } = 3;
        VipBalance _balance;
        /// <summary>Данные визитов. Читаются при каждом событии — правка во время Play действует со следующего.</summary>
        public VipBalance Balance => _balance != null ? _balance : (_balance = BalanceData.LoadOrDefault<VipBalance>(VipBalance.File));
        Shuttle _shuttle;
        float _arrivedHour = -1f;
        readonly System.Random _rnd = new System.Random(77);
        int _visitCount;

        public void Init(int seed)
        {
            NextVisitDay = Balance.firstVisitDay;
            GameEvents.HourPassed += OnHour;
        }

        void OnDestroy() { GameEvents.HourPassed -= OnHour; }

        void OnHour(int hour)
        {
            if (!Game.Config.enableVipVisits) return;
            if (Game.Tutorial != null && !Game.Tutorial.AllowVip) { NextVisitDay = Mathf.Max(NextVisitDay, Game.Clock.Day + 2); return; }
            if (Pending != null && !Pending.Resolved && Game.Clock.TotalHours >= Pending.ResolveAtHour) Resolve(Pending);
            // гость не дождался встречи — улетает, считая это отказом
            if (Current != null && _arrivedHour >= 0f && Game.Clock.TotalHours - _arrivedHour > Balance.patienceHours)
            {
                Game.Log?.Center($"{Current.Title} {Current.Name} не дождался(ась) встречи с командиром и улетает.");
                foreach (var q in Current.Requests) if (q.Choice < 0) q.Choice = 2;
                Depart(Current);
            }
            if (Current == null && Pending == null && Game.Clock.Day >= NextVisitDay && hour == Balance.arrivalHour) BeginVisit();
        }

        public void ForceVisit() { if (Current == null) BeginVisit(); }

        void BeginVisit()
        {
            var entry = PickVisit();
            if (entry == null) { NextVisitDay = Game.Clock.Day + 1; return; }
            _visitCount++;
            Current = entry.Create((int)Game.Rank.Current);
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

        /// <summary>Выбор визита: первый — первая запись (если так задано), дальше — случайно по весам среди доступных по рангу.</summary>
        VipVisitEntry PickVisit()
        {
            var b = Balance;
            if (b.visits.Count == 0) return null;
            if (_visitCount == 0 && b.firstVisitIsFirstEntry && b.visits[0] != null) return b.visits[0];
            var rank = Game.Rank.Current;
            float total = 0f;
            foreach (var e in b.visits) if (e != null && e.weight > 0f && rank >= e.minRank) total += e.weight;
            if (total <= 0f) return null;
            double roll = _rnd.NextDouble() * total;
            VipVisitEntry last = null;
            foreach (var e in b.visits)
            {
                if (e == null || e.weight <= 0f || rank < e.minRank) continue;
                last = e;
                roll -= e.weight;
                if (roll <= 0) return e;
            }
            return last;
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
                Portrait = v.Portrait,
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
            var b = Balance;
            float q = 0f;
            int refused = 0;
            foreach (var r in v.Requests)
            {
                q += r.Choice == 0 ? 1f : r.Choice == 1 ? b.junkQuality : 0f;
                if (r.Choice == 2) refused++;
            }
            v.Quality = v.Requests.Count > 0 ? q / v.Requests.Count : 0f;
            v.Refused = refused == v.Requests.Count;

            if (v.Refused)
            {
                Game.Rank.AddReputation(-b.refuseReputationPenalty, $"отказ {v.Title.ToLower()}у");
                Game.Supply.SkipNext++;
                Game.Log?.Center($"{v.Title} {v.Name} улетает ни с чем. «Я доложу о вашем отношении, офицер».");
            }
            else if (v.Quality < b.stingyQuality)
            {
                Game.Rank.AddReputation(-b.stingyReputationPenalty, "скупость при обеспечении миссии");
                Game.Log?.Center($"{v.Title} {v.Name} с недовольным видом забирает выданное.");
            }
            else
            {
                Game.Rank.AddReputation(b.satisfiedReputation, "обеспечение миссии");
                Game.Log?.Center($"{v.Title} {v.Name}: «Отличная работа, аванпост. Я этого не забуду».");
            }
            v.ResolveAtHour = Game.Clock.TotalHours + Mathf.Lerp(b.missionHoursMin, Mathf.Max(b.missionHoursMin, b.missionHoursMax), (float)_rnd.NextDouble());
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
            int minD = Mathf.Max(1, b.intervalMinDays), maxD = Mathf.Max(minD, b.intervalMaxDays);
            NextVisitDay = Game.Clock.Day + minD + _rnd.Next(maxD - minD + 1);
        }

        /// <summary>Исход миссии высокого чина.</summary>
        void Resolve(VipVisit v)
        {
            var b = Balance;
            v.Resolved = true;
            Pending = null;
            float success = Mathf.Clamp01(b.successBase + b.successPerQuality * v.Quality);
            bool ok = _rnd.NextDouble() < success;
            var returnAt = Game.World.LandingZone;
            if (ok)
            {
                int rep = Mathf.RoundToInt(b.successReputationBase + b.successReputationPerQuality * v.Quality);
                Game.Rank.AddReputation(rep, $"успех миссии: {v.Title.ToLower()} {v.Name}");
                Game.Rank.AddPoints(Mathf.RoundToInt(b.successRankPointsBase + b.successRankPointsPerQuality * v.Quality), "благодарность высокого чина");
                if (v.Quality >= b.rewardQuality && (!string.IsNullOrEmpty(v.RewardFlag) || v.RewardCredits > 0))
                {
                    if (!string.IsNullOrEmpty(v.RewardFlag)) Game.Entry.SetFlag(v.RewardFlag);
                    if (v.RewardCredits > 0) Game.Bank.Add(ResourceType.Credits, v.RewardCredits, true);
                    if (!string.IsNullOrEmpty(v.RewardMessage)) Game.Log?.Center(v.RewardMessage);
                }
                foreach (var n in v.LentNpcs) if (n != null) { n.ReturnFromAway(returnAt); n.AddMorale(b.lentNpcMoraleOnSuccess); Game.Log?.Info($"{n.DisplayName} вернулся(ась) с миссии."); }
                Game.Log?.Center($"Миссия «{v.Title} {v.Name}» успешна.");
            }
            else
            {
                Game.Log?.Alert($"Центр: {v.Title.ToLower()} {v.Name} погиб(ла) при выполнении миссии.");
                foreach (var n in v.LentNpcs)
                {
                    if (n == null) continue;
                    if (_rnd.NextDouble() < b.survivorChance) { n.ReturnFromAway(returnAt); n.AddMorale(b.survivorMorale); Game.Log?.Warn($"{n.DisplayName} вернулся(ась) — единственный(ая) выживший(ая)."); }
                    else { n.ReturnFromAway(returnAt); n.Health.TakeDamage(new DamageInfo(9999f, DamageType.Melee, n.transform.position, Faction.Hostile)); }
                }
                if (v.Quality < b.tribunalQuality || v.Refused)
                {
                    Game.Rank.Demote($"гибель {v.Title.ToLower()}а {v.Name} при недостаточном обеспечении миссии");
                    Game.Rank.AddReputation(-b.tribunalReputationPenalty, "трибунал");
                }
                else Game.Rank.AddReputation(-b.failReputationPenalty, "гибель высокого чина (обеспечение было достаточным)");
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
