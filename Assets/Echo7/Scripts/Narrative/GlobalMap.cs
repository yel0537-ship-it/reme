using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum RegionKind { Home, Mine, Station, Expedition, Locked }

    public class GlobalRegion
    {
        public string Id;
        public string Name;
        public string Description;
        public RegionKind Kind;
        public Feature RequiredFeature = Feature.GlobalMap;
        public bool NeedsSpaceport;
        public Vector2 MapPos;          // 0..1 на схеме планеты
        public bool Established;        // рудник основан
        public bool Busy;               // экспедиция/рейс в процессе
        public float BusyUntilHour;
    }

    /// <summary>Торговая позиция станции.</summary>
    public class TradeOffer
    {
        public ResourceType Type;
        public int BuyPrice;   // кредитов за 10 ед.
        public int SellPrice;  // кредитов за 10 ед.
        public bool Closed;    // только закрытый рынок
    }

    /// <summary>
    /// Глобальный слой: второй аванпост-рудник (Капитан), орбитальная станция (торговля, высокие чины),
    /// экспедиции челноком в новые районы планеты (Майор + космопорт).
    /// </summary>
    public class GlobalMap : MonoBehaviour
    {
        public readonly List<GlobalRegion> Regions = new List<GlobalRegion>();
        public readonly List<TradeOffer> Market = new List<TradeOffer>();

        public void Init()
        {
            Regions.Add(new GlobalRegion { Id = "home", Name = "Аванпост «Эхо-7»", Kind = RegionKind.Home, MapPos = new Vector2(0.45f, 0.55f), Description = "Ваш аванпост в центре саванны." });
            Regions.Add(new GlobalRegion { Id = "mine", Name = "Рудник «Глубина»", Kind = RegionKind.Mine, RequiredFeature = Feature.SecondOutpost, MapPos = new Vector2(0.68f, 0.38f),
                Description = "Второй аванпост у богатой титановой жилы. После основания ежедневно присылает титан и кристаллы. Основание: 80 сплавов, 20 титана, 1 боец." });
            Regions.Add(new GlobalRegion { Id = "station", Name = "Орбитальная станция «Вега»", Kind = RegionKind.Station, RequiredFeature = Feature.GlobalMap, NeedsSpaceport = true, MapPos = new Vector2(0.85f, 0.85f),
                Description = "Торговля ресурсами за кредиты, встречи с высшим командованием. Закрытый рынок — для тех, кому доверяют." });
            Regions.Add(new GlobalRegion { Id = "canyon", Name = "Каньон Кейна", Kind = RegionKind.Expedition, RequiredFeature = Feature.GlobalMap, NeedsSpaceport = true, MapPos = new Vector2(0.22f, 0.3f),
                Description = "Недоступный по земле район. Экспедиция челноком (12 ч, 2 бойца) может вернуться с редкими ресурсами — или не вернуться." });
            Regions.Add(new GlobalRegion { Id = "plateau", Name = "Северное плато", Kind = RegionKind.Locked, MapPos = new Vector2(0.5f, 0.12f),
                Description = "Заброшенные автоматические заводы ИИ. Центр запрещает высадку. (Задел для синтетической угрозы из ГДД.)" });

            Market.Add(new TradeOffer { Type = ResourceType.Alloys, BuyPrice = 12, SellPrice = 6 });
            Market.Add(new TradeOffer { Type = ResourceType.Ammo, BuyPrice = 8, SellPrice = 4 });
            Market.Add(new TradeOffer { Type = ResourceType.Rations, BuyPrice = 10, SellPrice = 5 });
            Market.Add(new TradeOffer { Type = ResourceType.Titanium, BuyPrice = 40, SellPrice = 22 });
            Market.Add(new TradeOffer { Type = ResourceType.Crystals, BuyPrice = 70, SellPrice = 38 });
            Market.Add(new TradeOffer { Type = ResourceType.Silicon, BuyPrice = 25, SellPrice = 12 });
            Market.Add(new TradeOffer { Type = ResourceType.Medkits, BuyPrice = 60, SellPrice = 25, Closed = true });
            Market.Add(new TradeOffer { Type = ResourceType.Water, BuyPrice = 15, SellPrice = 3, Closed = true });

            GameEvents.DayStarted += OnDay;
            GameEvents.HourPassed += OnHour;
        }

        public GlobalRegion Get(string id) => Regions.Find(r => r.Id == id);

        public string LockReason(GlobalRegion r)
        {
            if (r.Kind == RegionKind.Locked) return "Высадка запрещена Центром";
            if (r.Kind == RegionKind.Home) return null;
            if (!Game.Rank.IsUnlocked(r.RequiredFeature)) return Game.Rank.LockReason(r.RequiredFeature);
            if (r.NeedsSpaceport)
            {
                var sp = StructureLookup.FirstOf(StructureKind.Spaceport);
                if (sp == null || !sp.IsBuilt) return "Нужен построенный космопорт";
            }
            if (r.Busy) return $"Рейс в пути (~{Mathf.Max(0f, r.BusyUntilHour - Game.Clock.TotalHours):0} ч)";
            return null;
        }

        void OnDay(int day)
        {
            var mine = Get("mine");
            if (mine != null && mine.Established)
            {
                Game.Supply.SendBonusPod(new Cost((ResourceType.Titanium, 18), (ResourceType.Crystals, 6)), "Груз с рудника «Глубина»");
            }
        }

        void OnHour(int h)
        {
            foreach (var r in Regions)
            {
                if (!r.Busy || Game.Clock.TotalHours < r.BusyUntilHour) continue;
                r.Busy = false;
                if (r.Kind == RegionKind.Expedition) ResolveExpedition(r);
            }
        }

        public bool EstablishMine()
        {
            var r = Get("mine");
            if (r == null || r.Established || LockReason(r) != null) return false;
            var cost = new Cost((ResourceType.Alloys, 80), (ResourceType.Titanium, 20));
            if (!Game.Bank.Spend(cost)) { Game.Log?.Warn(Game.Bank.MissingText(cost)); return false; }
            GarrisonNpc worker = null;
            foreach (var n in Registry.Npcs) if (n != null && !n.Health.IsDead && !n.IsAway && n.Role != NpcRole.Medic) { worker = n; break; }
            if (worker == null) { Game.Bank.Add(cost); Game.Log?.Warn("Нужен свободный боец для рудника."); return false; }
            worker.SendAway();
            r.Established = true;
            Game.Log?.Center($"Основан рудник «Глубина». {worker.DisplayName} назначен(а) управляющим.");
            Game.Rank.AddPoints(60, "основание второго аванпоста");
            return true;
        }

        public bool StartExpedition()
        {
            var r = Get("canyon");
            if (r == null || LockReason(r) != null) return false;
            var crew = new List<GarrisonNpc>();
            foreach (var n in Registry.Npcs) if (n != null && !n.Health.IsDead && !n.IsAway && n.Role == NpcRole.Soldier && crew.Count < 2) crew.Add(n);
            if (crew.Count < 2) { Game.Log?.Warn("Нужно двое свободных солдат."); return false; }
            if (!Game.Bank.Spend(new Cost((ResourceType.Ammo, 60), (ResourceType.Rations, 10)))) { Game.Log?.Warn("Нужно 60 патронов и 10 пайков."); return false; }
            foreach (var n in crew) n.SendAway();
            _expeditionCrew = crew;
            r.Busy = true;
            r.BusyUntilHour = Game.Clock.TotalHours + 12f;
            Game.Log?.Center("Челнок с экспедицией вылетел в Каньон Кейна.");
            return true;
        }

        List<GarrisonNpc> _expeditionCrew = new List<GarrisonNpc>();

        void ResolveExpedition(GlobalRegion r)
        {
            bool ok = Random.value < 0.7f;
            var at = StructureLookup.FirstOf(StructureKind.Spaceport)?.transform.position ?? Game.World.LandingZone;
            foreach (var n in _expeditionCrew)
            {
                if (n == null) continue;
                n.ReturnFromAway(at + Random.insideUnitSphere * 3f);
                if (!ok && Random.value < 0.5f) n.Health.TakeDamage(new DamageInfo(9999f, DamageType.Melee, at, Faction.Hostile));
            }
            _expeditionCrew.Clear();
            if (ok)
            {
                Game.Bank.Add(ResourceType.Crystals, 40, true);
                Game.Bank.Add(ResourceType.Titanium, 30, true);
                Game.Bank.Add(ResourceType.Credits, 80, true);
                Game.Rank.AddPoints(50, "успешная экспедиция");
                Game.Log?.Center("Экспедиция вернулась с добычей из Каньона Кейна!");
            }
            else Game.Log?.Alert("Экспедиция попала в засаду в каньоне. Груз потерян.");
        }

        public bool CanTrade => LockReason(Get("station")) == null;

        public string TradeLock(TradeOffer o)
        {
            if (!CanTrade) return LockReason(Get("station"));
            if (o.Closed && !Game.Entry.HasFlag("market_closed")) return "Закрытый рынок";
            return null;
        }

        public void Buy(TradeOffer o)
        {
            if (TradeLock(o) != null) return;
            if (!Game.Bank.Spend(ResourceType.Credits, o.BuyPrice)) { Game.Log?.Warn("Недостаточно кредитов."); return; }
            Game.Bank.Add(o.Type, o.Type == ResourceType.Medkits ? 2 : 10);
            Game.Sound?.Play2D(Sfx.UiClick);
        }

        public void Sell(TradeOffer o)
        {
            if (TradeLock(o) != null) return;
            int amount = o.Type == ResourceType.Medkits ? 2 : 10;
            if (!Game.Bank.Spend(o.Type, amount)) { Game.Log?.Warn("Недостаточно ресурса."); return; }
            Game.Bank.Add(ResourceType.Credits, o.SellPrice);
            Game.Sound?.Play2D(Sfx.UiClick);
        }
    }
}
