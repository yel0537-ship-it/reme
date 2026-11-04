using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public partial class UiManager
    {
        Rect WindowRect(float w, float h) => new Rect(W / 2 - w / 2, Mathf.Max(100f, H / 2 - h / 2 - 40f), w, h);

        bool CloseButton(Rect r)
        {
            return GUI.Button(new Rect(r.xMax - 40, r.y + 9, 28, 24), "✕", UiSkin.Tab);
        }

        // ------------------------------------------------------------ строительство

        static readonly BuildCategory[] BuildCats = { BuildCategory.Walls, BuildCategory.Defense, BuildCategory.Power, BuildCategory.Base, BuildCategory.Industry, BuildCategory.Logistics };

        static bool VisibleInBuildMenu(BuildingDef d) => d.Buildable && (Game.Tutorial == null || Game.Tutorial.IsBuildUnlocked(d.Kind));

        static bool CategoryHasVisible(BuildCategory c)
        {
            foreach (var d in BuildingCatalog.All) if (d.Category == c && VisibleInBuildMenu(d)) return true;
            return false;
        }

        /// <summary>При открытии меню стройки во время обучения — сразу нужная вкладка.</summary>
        void AutoBuildTab()
        {
            var tut = Game.Tutorial;
            if (tut != null && tut.Active && tut.HighlightKind.HasValue)
            {
                var def = BuildingCatalog.Get(tut.HighlightKind.Value);
                for (int i = 0; i < BuildCats.Length; i++) if (BuildCats[i] == def.Category) { _buildTab = i; return; }
            }
            if (!CategoryHasVisible(BuildCats[_buildTab]))
                for (int i = 0; i < BuildCats.Length; i++) if (CategoryHasVisible(BuildCats[i])) { _buildTab = i; return; }
        }

        void DrawBuildWindow()
        {
            var tut = Game.Tutorial;
            var r = new Rect(12, _leftColumnBottom, 440, 560);
            r.height = Mathf.Min(r.height, H - r.y - 230);
            if (r.height < 240f) { r.y = Mathf.Max(12f, H - 230 - 240); r.height = 240f; }
            Block(r);
            var content = UiSkin.Window(r, "Строительство", $"РАДИУС БАЗЫ {Game.Rank.BuildRadius:0} М");
            if (CloseButton(r)) Close();
            var highlightCat = tut != null && tut.Active && tut.HighlightKind.HasValue ? BuildingCatalog.Get(tut.HighlightKind.Value).Category : (BuildCategory?)null;
            float tw = (r.width - 24) / 3f;
            for (int i = 0; i < BuildCats.Length; i++)
            {
                var tr = new Rect(r.x + 12 + (i % 3) * tw, content.y - 2 + (i / 3) * 26, tw - 4, 24);
                bool any = CategoryHasVisible(BuildCats[i]);
                var style = !any ? UiSkin.ButtonDisabled : _buildTab == i ? UiSkin.TabActive : UiSkin.Tab;
                if (GUI.Button(tr, BuildingCatalog.CategoryName(BuildCats[i]).ToUpperInvariant(), style) && any) { _buildTab = i; _scroll = Vector2.zero; }
                if (highlightCat.HasValue && BuildCats[i] == highlightCat.Value && _buildTab != i) UiSkin.Highlight(tr);
            }
            var list = new List<BuildingDef>();
            foreach (var d in BuildingCatalog.All) if (d.Category == BuildCats[_buildTab] && VisibleInBuildMenu(d)) list.Add(d);
            var view = new Rect(r.x + 8, content.y + 54, r.width - 16, r.yMax - content.y - 62);
            if (view.Contains(_mouseGui)) WantsScroll = true;
            float rowH = 78f;
            if (list.Count == 0)
                GUI.Label(new Rect(view.x + 8, view.y + 8, view.width - 16, 40), UiSkin.Col("Постройки этого раздела откроются по ходу обучения.", UiSkin.Dim), UiSkin.Small);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 20, list.Count * rowH));
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                var row = new Rect(0, i * rowH, view.width - 22, rowH - 6);
                string lockR = Game.Build.LockReason(d);
                var cost = Game.Build.CostOf(d);
                bool afford = Game.Bank.CanAfford(cost);
                var style = lockR != null ? UiSkin.ButtonDisabled : Game.Build.Selected == d ? UiSkin.ButtonAccent : UiSkin.Button;
                if (GUI.Button(row, GUIContent.none, style) && lockR == null) Game.Build.Begin(d);
                UiSkin.Fill(new Rect(row.x + 4, row.y + 8, 2, row.height - 16), lockR != null ? UiSkin.Dim : afford ? UiSkin.Cyan : UiSkin.Bad);
                GUI.Label(new Rect(row.x + 12, row.y + 5, row.width - 20, 20), $"<b>{d.Name}</b>" + (d.NeedsPower ? UiSkin.Col($"  ⚡{d.EnergyUse:0}", UiSkin.Cyan) : d.EnergyProduce > 0 ? UiSkin.Col($"  +{d.EnergyProduce:0}⚡", UiSkin.Good) : ""), UiSkin.Label);
                GUI.Label(new Rect(row.x + 12, row.y + 25, row.width - 20, 32), d.Description, UiSkin.Tiny);
                string costText = lockR ?? cost.ToString();
                GUI.Label(new Rect(row.x + 12, row.y + 53, row.width - 20, 18), UiSkin.Col(costText, lockR != null ? UiSkin.Bad : afford ? UiSkin.Accent : UiSkin.Bad), new GUIStyle(UiSkin.MonoSmall) { richText = true });
                if (tut != null && tut.Active && tut.HighlightKind == d.Kind && Game.Build.Selected != d) UiSkin.Highlight(new Rect(row.x + 6, row.y + 4, row.width - 12, row.height - 8));
            }
            GUI.EndScrollView();
        }

        // ------------------------------------------------------------ инспектор построек

        void DrawInspector()
        {
            var s = Inspected;
            if (s == null || s.IsDestroyed) return;
            var r = new Rect(W - 460, H - BottomPanelH - 16 - 14 - 190 - 16 - 306, 440, 306);
            r.y = Mathf.Max(260, r.y);
            Block(r);
            UiSkin.Window(r, s.Def.Name, s.IsBuilt ? "ОБЪЕКТ ИСПРАВЕН" : "ИДЁТ МОНТАЖ");
            if (CloseButton(r)) { Inspected = null; return; }
            var hc = UiSkin.HealthColor(s.Health.Fraction);
            UiSkin.SegBar(new Rect(r.x + 16, r.y + 52, r.width - 32, 8), s.Health.Fraction, 30, hc, 2f);
            GUI.Label(new Rect(r.x + 16, r.y + 62, r.width - 32, 20), $"ПРОЧНОСТЬ {Mathf.CeilToInt(s.Health.Current)}/{Mathf.CeilToInt(s.Health.Max)}" + (s.Health.DamageReduction > 0 ? UiSkin.Col($"  · ЗАЩИТА {Mathf.RoundToInt(s.Health.DamageReduction * 100)}%", UiSkin.Cyan) : ""), new GUIStyle(UiSkin.MonoSmall) { richText = true });
            GUI.Label(new Rect(r.x + 16, r.y + 84, r.width - 32, 80), s.StatusText(), UiSkin.Small);

            float y = r.y + 166;
            float bw = (r.width - 32) / 2f;
            // действия по типу
            if (s is Turret t && t.IsBuilt)
            {
                GUI.Label(new Rect(r.x + 12, y, r.width - 24, 20), "Расчёт:", UiSkin.Small);
                y += 20;
                var candidates = new List<GarrisonNpc>();
                foreach (var n in Registry.Npcs) if (n != null && !n.Health.IsDead && !n.IsAway) candidates.Add(n);
                int col = 0;
                foreach (var n in candidates)
                {
                    if (col >= 4) break;
                    bool isCrew = t.Crew == n;
                    if (GUI.Button(new Rect(r.x + 12 + col * ((r.width - 24) / 4f), y, (r.width - 24) / 4f - 4, 24), (isCrew ? "✔ " : "") + n.Name, isCrew ? UiSkin.TabActive : UiSkin.Tab))
                    {
                        if (isCrew) Game.Defense.ClearCrew(t); else Game.Defense.AssignCrew(t, n);
                    }
                    col++;
                }
                y += 30;
            }
            if (s is DroneBay db && db.IsBuilt)
            {
                if (GUI.Button(new Rect(r.x + 12, y, r.width - 24, 26), $"Приоритет добычи: {db.PriorityName} (сменить)", UiSkin.Button)) db.CyclePriority();
                y += 32;
            }
            if (s is CommandModule)
            {
                if (GUI.Button(new Rect(r.x + 12, y, r.width - 24, 26), "Связь с Центром", UiSkin.ButtonAccent)) OpenCenterComms();
                y += 32;
            }
            if (s is Spaceport sp && sp.IsBuilt)
            {
                if (GUI.Button(new Rect(r.x + 12, y, r.width - 24, 26), "Глобальная карта", UiSkin.ButtonAccent)) Open(UiWindow.GlobalMap);
                y += 32;
            }
            y = Mathf.Max(y, r.yMax - 40);
            if (s.NeedsService && s.IsBuilt)
            {
                if (GUI.Button(new Rect(r.x + 12, y, bw, 28), "Обслужить сейчас", UiSkin.Button)) s.Service();
            }
            if (s.Def.Buildable)
            {
                bool confirm = Time.unscaledTime < _demolishConfirmUntil;
                var refund = s.Def.Cost.Scaled(s.IsBuilt ? 0.5f : 1f);
                if (GUI.Button(new Rect(r.x + 20 + bw, y, bw, 28), confirm ? UiSkin.Col("Точно снести?", UiSkin.Bad) : $"Снести (+{refund})", UiSkin.Button))
                {
                    if (confirm) { s.Demolish(); Inspected = null; }
                    else _demolishConfirmUntil = Time.unscaledTime + 3f;
                }
            }
        }

        // ------------------------------------------------------------ навыки

        void DrawSkillsWindow()
        {
            var r = WindowRect(960, 560);
            Block(r);
            UiSkin.Window(r, "Дерево навыков", $"СВОБОДНЫХ ОЧКОВ: {Game.Skills.Points}");
            if (CloseButton(r)) Close();
            var branches = new[] { SkillBranch.Engineering, SkillBranch.Tactics, SkillBranch.Survival };
            float cw = (r.width - 48) / 3f;
            for (int b = 0; b < 3; b++)
            {
                float x = r.x + 16 + b * (cw + 8);
                GUI.Label(new Rect(x, r.y + 52, cw, 24), SkillTree.BranchName(branches[b]).ToUpperInvariant(), UiSkin.Header);
                int row = 0;
                foreach (var d in SkillTree.Defs)
                {
                    if (d.Branch != branches[b]) continue;
                    var box = new Rect(x, r.y + 84 + row * 112, cw, 104);
                    GUI.Box(box, GUIContent.none, UiSkin.PanelLight);
                    int lvl = Game.Skills.Level(d.Id);
                    GUI.Label(new Rect(box.x + 8, box.y + 4, box.width - 16, 22), $"<b>{d.Name}</b>", UiSkin.Label);
                    for (int p = 0; p < d.MaxLevel; p++)
                        UiSkin.Tex(new Rect(box.xMax - 14 - (d.MaxLevel - p) * 14, box.y + 9, 11, 11), UiSkin.Diamond, p < lvl ? UiSkin.Accent : new Color(0.25f, 0.32f, 0.36f));
                    GUI.Label(new Rect(box.x + 8, box.y + 26, box.width - 16, 20), d.Description, UiSkin.Tiny);
                    GUI.Label(new Rect(box.x + 8, box.y + 44, box.width - 16, 20), lvl > 0 ? UiSkin.Col(d.Effect(lvl), UiSkin.Good) : UiSkin.Col("Не изучено", UiSkin.Dim), UiSkin.Small);
                    string why = Game.Skills.LockReason(d.Id);
                    var br = new Rect(box.x + 8, box.y + 70, box.width - 16, 26);
                    if (why == null)
                    {
                        if (GUI.Button(br, $"Изучить → {d.Effect(lvl + 1)}", UiSkin.ButtonAccent)) Game.Skills.TryUpgrade(d.Id);
                    }
                    else GUI.Label(br, UiSkin.Col(why, why == "Максимум" ? UiSkin.Good : UiSkin.Dim), new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
                    row++;
                }
            }
        }

        // ------------------------------------------------------------ задачи и ранги

        void DrawTasksWindow()
        {
            var r = WindowRect(920, 600);
            Block(r);
            UiSkin.Window(r, "Центр: задачи и снабжение", $"РЕПУТАЦИЯ {Game.Rank.Reputation}/100");
            if (CloseButton(r)) Close();
            float lx = r.x + 16, lw = 540;
            float y = r.y + 50;
            var board = Game.Tasks;
            foreach (var t in board.Active.ToArray())
            {
                var box = new Rect(lx, y, lw, 108);
                GUI.Box(box, GUIContent.none, UiSkin.PanelLight);
                GUI.Label(new Rect(box.x + 10, box.y + 6, 400, 22), $"<b>{t.Title}</b>", UiSkin.Label);
                GUI.Label(new Rect(box.x + 330, box.y + 6, 200, 22), t.DeadlineDay > 0 ? $"срок: конец дня {t.DeadlineDay}" : "без срока", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.UpperRight });
                GUI.Label(new Rect(box.x + 10, box.y + 28, box.width - 20, 36), t.Description, UiSkin.Small);
                UiSkin.SegBar(new Rect(box.x + 10, box.y + 66, 400, 8), t.Fraction, 25, UiSkin.Cyan, 2f);
                GUI.Label(new Rect(box.x + 418, box.y + 60, 100, 20), $"{t.Progress} / {t.Target}", UiSkin.Small);
                string reward = $"Награда: {t.RewardRp} ОР" + (t.RewardRep != 0 ? $", репутация +{t.RewardRep}" : "") + (t.RewardSupplies != null && !t.RewardSupplies.IsEmpty ? $", {t.RewardSupplies}" : "");
                GUI.Label(new Rect(box.x + 10, box.y + 80, 420, 22), UiSkin.Col(reward, UiSkin.Accent), UiSkin.Tiny);
                if (board.CanDecline && GUI.Button(new Rect(box.xMax - 110, box.y + 78, 100, 24), "Отказаться", UiSkin.Tab)) board.Decline(t);
                y += 116;
            }
            if (board.Active.Count == 0) GUI.Label(new Rect(lx, y, lw, 24), "Новых задач нет. Центр выйдет на связь утром.", UiSkin.Small);

            // правая колонка — ранги
            float rx = r.x + 572, rw = r.width - 588;
            float ry = r.y + 50;
            GUI.Label(new Rect(rx, ry, rw, 24), "РАНГИ И ДОПУСКИ", UiSkin.Header);
            ry += 28;
            foreach (Rank rk in System.Enum.GetValues(typeof(Rank)))
            {
                bool have = Game.Rank.Current >= rk;
                var lines = new List<string>();
                foreach (Feature f in System.Enum.GetValues(typeof(Feature)))
                    if (PlayerRankManager.RequiredRank(f) == rk && f != Feature.BasicConstruction) lines.Add(PlayerRankManager.FeatureName(f));
                if (rk == Rank.Specialist) lines.Add("Малый отряд, базовые постройки");
                string head = (have ? UiSkin.Col("✔ ", UiSkin.Good) : "   ") + $"<b>{PlayerRankManager.RankName(rk)}</b>";
                GUI.Label(new Rect(rx, ry, rw, 20), head, UiSkin.Label);
                ry += 20;
                GUI.Label(new Rect(rx + 14, ry, rw - 14, 40), UiSkin.Col(string.Join(", ", lines), have ? UiSkin.Text : UiSkin.Dim), UiSkin.Tiny);
                ry += 36;
            }
            ry += 6;
            GUI.Label(new Rect(rx, ry, rw, 24), "СНАБЖЕНИЕ", UiSkin.Header);
            ry += 24;
            GUI.Label(new Rect(rx, ry, rw, 80), $"Плановая поставка через ~{Mathf.Max(0, Game.Supply.HoursToDelivery):0} ч:\n{Game.Supply.PlannedDelivery()}\nПрофиль: {DialogueLibrary.PriorityName(Game.Supply.Priority)}" +
                (Game.Supply.SkipNext > 0 ? UiSkin.Col("\nСледующая поставка будет пропущена!", UiSkin.Bad) : ""), UiSkin.Small);
            if (GUI.Button(new Rect(rx, r.yMax - 44, rw, 30), "Связаться с Центром", UiSkin.ButtonAccent)) OpenCenterComms();
        }

        // ------------------------------------------------------------ гарнизон

        void DrawGarrisonWindow()
        {
            var r = WindowRect(940, 560);
            Block(r);
            var g = Game.Garrison;
            UiSkin.Window(r, "Гарнизон", $"ЛИЧНЫЙ СОСТАВ {g.PresentCount}");
            if (CloseButton(r)) Close();
            var bedsC = g.PresentCount <= g.Beds ? UiSkin.Good : UiSkin.Bad;
            GUI.Label(new Rect(r.x + 16, r.y + 46, r.width - 32, 22),
                $"Средняя мораль: <b>{Mathf.RoundToInt(g.AverageMorale)}</b> ({g.MoraleWord(g.AverageMorale)})   ·   Койки: {UiSkin.Col($"{g.PresentCount}/{g.Beds}", bedsC)}   ·   Еда: {Mathf.RoundToInt(g.FoodFactor * 100)}%   ·   Вода: {Mathf.RoundToInt(g.WaterFactor * 100)}%",
                UiSkin.Small);
            GUI.Label(new Rect(r.x + 16, r.y + 68, r.width - 32, 22), UiSkin.Col("Мораль расчёта влияет на точность турели. При морали ниже 20 бойцы отказываются от постов, ниже 12 — могут дезертировать.", UiSkin.Dim), UiSkin.Tiny);
            string[] heads = { "Боец", "Здоровье", "Мораль", "Состояние", "" };
            float[] cols = { 0, 230, 340, 450, 640 };
            float y = r.y + 92;
            for (int i = 0; i < heads.Length; i++) GUI.Label(new Rect(r.x + 16 + cols[i], y, 200, 20), heads[i].ToUpperInvariant(), UiSkin.Header);
            y += 24;
            var view = new Rect(r.x + 8, y, r.width - 16, r.height - (y - r.y) - 10);
            if (view.Contains(_mouseGui)) WantsScroll = true;
            var list = new List<GarrisonNpc>(Registry.Npcs);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 20, list.Count * 40));
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                if (n == null) continue;
                float ry = i * 40;
                UiSkin.Fill(new Rect(0, ry, view.width - 22, 36), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, i % 2 == 0 ? 0.05f : 0.015f));
                GUI.Label(new Rect(8 + cols[0], ry + 8, 220, 22), $"<b>{n.Name}</b> {UiSkin.Col(n.RoleName, UiSkin.Dim)}" + (n.Sickness > 0f ? UiSkin.Col(" (болен)", UiSkin.Bad) : ""), UiSkin.Small);
                UiSkin.Bar(new Rect(8 + cols[1], ry + 15, 90, 7), n.Health.Fraction, UiSkin.Good);
                var mc = n.Morale >= 50 ? UiSkin.Good : n.Morale >= 25 ? UiSkin.Accent : UiSkin.Bad;
                UiSkin.Bar(new Rect(8 + cols[2], ry + 15, 90, 7), n.Morale / 100f, mc);
                GUI.Label(new Rect(8 + cols[3], ry + 8, 190, 22), n.StateText, UiSkin.Small);
                if (n.IsAway) continue;
                if (n.Post == null)
                {
                    var ab = new Rect(8 + cols[4], ry + 5, 130, 26);
                    if (GUI.Button(ab, "На турель", UiSkin.Tab)) g.AutoAssign(n);
                    var tut = Game.Tutorial;
                    if (tut != null && tut.Active && tut.HighlightWindow == UiWindow.Garrison && g.FreeTurrets().Count > 0) UiSkin.Highlight(ab);
                }
                else if (GUI.Button(new Rect(8 + cols[4], ry + 5, 130, 26), "Снять с поста", UiSkin.Tab)) Game.Defense.ClearCrew(n.Post);
                if (GUI.Button(new Rect(8 + cols[4] + 136, ry + 5, 100, 26), "Говорить", UiSkin.Tab)) Game.Dialogue.Start(DialogueLibrary.NpcTalk(n));
            }
            GUI.EndScrollView();
        }

        // ------------------------------------------------------------ глобальная карта

        void DrawGlobalMapWindow()
        {
            var r = WindowRect(1000, 620);
            Block(r);
            UiSkin.Window(r, "Глобальная карта планеты", "ОРБИТАЛЬНЫЙ СНИМОК");
            if (CloseButton(r)) Close();
            var map = new Rect(r.x + 16, r.y + 50, 520, 540);
            GUI.Box(map, GUIContent.none, UiSkin.PanelLight);
            // схематичная планета
            var planet = new Rect(map.x + 20, map.y + 20, map.width - 40, map.height - 40);
            var old = GUI.color;
            GUI.color = new Color(0.55f, 0.48f, 0.3f, 0.35f);
            GUI.DrawTexture(planet, UiSkin.Dot);
            GUI.color = old;
            foreach (var reg in Game.Map.Regions)
            {
                var p = new Vector2(planet.x + reg.MapPos.x * planet.width, planet.y + reg.MapPos.y * planet.height);
                string lockR = Game.Map.LockReason(reg);
                var c = reg.Id == "home" ? UiSkin.Accent : lockR == null ? UiSkin.Cyan : UiSkin.Dim;
                if (reg.Established) c = UiSkin.Good;
                var dot = new Rect(p.x - 12, p.y - 12, 24, 24);
                GUI.color = c;
                GUI.DrawTexture(dot, UiSkin.Dot);
                GUI.color = old;
                if (GUI.Button(new Rect(p.x - 80, p.y + 12, 160, 22), reg.Name, _selectedRegion == reg.Id ? UiSkin.TabActive : UiSkin.Tab)) _selectedRegion = reg.Id;
            }
            // панель региона
            var sel = Game.Map.Get(_selectedRegion) ?? Game.Map.Regions[0];
            float x = r.x + 552, w = r.width - 568, y = r.y + 50;
            GUI.Label(new Rect(x, y, w, 24), sel.Name.ToUpperInvariant(), UiSkin.Header);
            y += 28;
            GUI.Label(new Rect(x, y, w, 90), sel.Description, UiSkin.Small);
            y += 92;
            string why = Game.Map.LockReason(sel);
            if (why != null && sel.Kind != RegionKind.Home) { GUI.Label(new Rect(x, y, w, 24), UiSkin.Col(why, UiSkin.Bad), UiSkin.Label); y += 30; }
            switch (sel.Kind)
            {
                case RegionKind.Mine:
                    if (sel.Established) GUI.Label(new Rect(x, y, w, 40), UiSkin.Col("Рудник работает: каждое утро присылает 18 титана и 6 кристаллов.", UiSkin.Good), UiSkin.Small);
                    else if (why == null && GUI.Button(new Rect(x, y, w, 30), "Основать рудник (80 сплавов, 20 титана, 1 боец)", UiSkin.ButtonAccent)) Game.Map.EstablishMine();
                    break;
                case RegionKind.Expedition:
                    if (why == null && GUI.Button(new Rect(x, y, w, 30), "Отправить экспедицию (2 солдата, 60 патронов, 10 пайков)", UiSkin.ButtonAccent)) Game.Map.StartExpedition();
                    break;
                case RegionKind.Station:
                    if (why == null) DrawStationTrade(new Rect(x, y, w, r.yMax - y - 14));
                    break;
            }
        }

        void DrawStationTrade(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y, r.width, 22), $"Торговля (кредиты: {UiSkin.Col(Game.Bank[ResourceType.Credits].ToString(), UiSkin.Accent)})", UiSkin.Label);
            float y = r.y + 28;
            foreach (var o in Game.Map.Market)
            {
                string lockR = Game.Map.TradeLock(o);
                int amount = o.Type == ResourceType.Medkits ? 2 : 10;
                GUI.Label(new Rect(r.x, y + 4, 150, 22), UiSkin.Col(ResourceBank.Name(o.Type), ResourceBank.ColorOf(o.Type)) + (o.Closed ? UiSkin.Col(" ★", UiSkin.Accent) : ""), UiSkin.Small);
                if (lockR != null) { GUI.Label(new Rect(r.x + 150, y + 4, r.width - 150, 22), UiSkin.Col(lockR, UiSkin.Dim), UiSkin.Tiny); y += 30; continue; }
                if (GUI.Button(new Rect(r.x + 150, y, 130, 26), $"Купить {amount} −{o.BuyPrice}кр", UiSkin.Tab)) Game.Map.Buy(o);
                if (GUI.Button(new Rect(r.x + 286, y, 130, 26), $"Продать {amount} +{o.SellPrice}кр", UiSkin.Tab)) Game.Map.Sell(o);
                y += 30;
            }
        }

        // ------------------------------------------------------------ орбитальная доставка

        void DrawOrbitalWindow()
        {
            var r = WindowRect(720, 500);
            Block(r);
            UiSkin.Window(r, "Орбитальная доставка", "ГРУЗОВОЙ КАНАЛ");
            if (CloseButton(r)) Close();
            GUI.Label(new Rect(r.x + 16, r.y + 48, r.width - 32, 22), $"Репутация: {Game.Rank.Reputation}   ·   Кредиты: {Game.Bank[ResourceType.Credits]}   ·   Груз садится на посадочную площадку.", UiSkin.Small);
            float y = r.y + 76;
            foreach (var o in Game.Supply.Catalog())
            {
                var box = new Rect(r.x + 16, y, r.width - 32, 58);
                GUI.Box(box, GUIContent.none, UiSkin.PanelLight);
                GUI.Label(new Rect(box.x + 10, box.y + 4, 420, 22), $"<b>{o.Name}</b>", UiSkin.Label);
                GUI.Label(new Rect(box.x + 10, box.y + 26, 440, 22), $"{o.Description}  ·  −{o.ReputationCost} реп., {o.CreditCost} кр, ~{o.EtaHours:0} ч", UiSkin.Tiny);
                string why = Game.Supply.CanOrder(o);
                if (why == null) { if (GUI.Button(new Rect(box.xMax - 150, box.y + 14, 140, 30), "Заказать", UiSkin.ButtonAccent)) Game.Supply.Order(o); }
                else GUI.Label(new Rect(box.xMax - 210, box.y + 18, 200, 24), UiSkin.Col(why, UiSkin.Bad), new GUIStyle(UiSkin.Tiny) { alignment = TextAnchor.UpperRight });
                y += 64;
            }
            if (Game.Supply.Pending.Count > 0)
                GUI.Label(new Rect(r.x + 16, r.yMax - 30, r.width - 32, 22), $"В пути: {Game.Supply.Pending.Count} заказ(а).", UiSkin.Small);
        }

        // ------------------------------------------------------------ справка

        void DrawHelpWindow()
        {
            var r = WindowRect(880, 640);
            Block(r);
            UiSkin.Window(r, "Справка: аванпост «Эхо-7»", "БАЗА ЗНАНИЙ");
            if (CloseButton(r)) Close();
            var view = new Rect(r.x + 16, r.y + 50, r.width - 32, r.height - 60);
            if (view.Contains(_mouseGui)) WantsScroll = true;
            const string text =
                "<b>Управление (как в Diablo 4)</b>\n" +
                "ЛКМ — идти / атаковать врага / взаимодействовать (удержание — идти за курсором)\n" +
                "ПКМ — стрелять в точку · WASD — прямое управление · Пробел — перекат · E — взаимодействие рядом\n" +
                "1 — граната · 2 — боевой дрон · 3 — аптечка · 4 — подрывной заряд (пещеры!)\n" +
                "B — строительство · K — навыки · J — задачи Центра · G — гарнизон · M — глобальная карта · Esc — пауза/закрыть\n" +
                "Колесо — зум. В режиме стройки: R — поворот, ПКМ — отмена, стены тянутся мышью, Shift — несколько построек.\n\n" +
                "<b>Обучение</b>\nПролог из 10 заданий предлагается при новой игре (Esc → Новая игра). Во время пролога время стоит, " +
                "а интерфейс открывается по шагам; «Брифинг» в панели цели повторит инструкции. После пролога о новых механиках рассказывают карточки «Новое».\n\n" +
                "<b>Цикл</b>\nДнём — разведка, добыча, стройка. Ночью приходят волны. Каждое действие шумит (стройка, выстрелы, буры, взрывы) — " +
                "шум копит «угрозу» и вызывает неожиданные волны с той стороны, откуда шумели. Взрыв завала пещеры почти гарантирует атаку.\n\n" +
                "<b>Ресурсы</b>\nЦентр присылает сплавы, пайки и патроны по утрам — объём зависит от репутации и выполненных задач. " +
                "Титан, кристаллы и кремний есть только на планете: добывайте вручную, дронами (дронпорт) или бурами. " +
                "Кремний — дилемма: жилые блоки (мораль) или лазеры/конденсаторы (оборона, вода).\n\n" +
                "<b>Оборона</b>\nПулемёты дешёвые и хороши против мелочи, лазеры пробивают броню «танков», тяжёлые турели бьют по площади. " +
                "Мины расходуются. Назначайте бойцов расчётами: мораль расчёта = точность турели. Если расчёт погиб, турель работает в ослабленном режиме. " +
                "Турели изнашиваются (особенно в пекло) и клинят — дроны обслуживают их сами.\n\n" +
                "<b>Второй ярус (с ранга Лейтенант)</b>\nНастил кладут на стены и опорные колонны (протяжка мышью), на 1 клетку он может выступать консолью. " +
                "Лестница (верхний конец — к настилу) — единственный путь наверх для гарнизона. В режиме стройки Q переключает ярус: турели, сенсоры и энергоузлы " +
                "можно ставить на стену или настил — дальность турелей +15%, пехота до них не достаёт (только прыгуны и стрелки), зато бьёт стену снизу. " +
                "Разрушили опору — всё, что над ней, обрушится. Снести опору под настилом нельзя: сначала снимите то, что сверху. " +
                "Обычный клик по настилу — пройти туда, Shift+клик — осмотр. Перекат (Пробел) перемахивает через перила — так можно спрыгнуть вниз.\n\n" +
                "<b>Угрозы</b>\nГрызуны портят мелкие постройки и перегрызают кабели (постройка обесточивается). Прыгуны перемахивают ограждения " +
                "(но не стены) и ломают механизмы: ворота заклинивает открытыми. Громилы проламывают слабые участки стен. Плевуны стреляют кислотой из-за стен. " +
                "Танк (с ранга Капитан) идёт прямо к реактору.\n\n" +
                "<b>Планета</b>\nПекло: вода испаряется, саванна выгорает в пустыню, растёт износ механизмов. Потом идут ливни — озёра и луга возвращаются. " +
                "Миграция насекомых вызывает лихорадку у тех, кто снаружи: держите гарнизон в жилых блоках, лечите в медблоке.\n\n" +
                "<b>Ранги</b>\nЛейтенант — тяжёлые и лазерные турели, геологоразведка, титановые стены. Капитан — право голоса в Центре, второй аванпост-рудник, " +
                "орбитальные доставки. Майор — космопорт, глобальная карта, полёты на станцию и экспедиции.\n\n" +
                "<b>Высокие чины</b>\nИнспекторы и генералы просят лучшую броню, медиков и транспорт. Помощь — репутация и уникальные чертежи. " +
                "Отказ — урезанные поставки. Если чин погибнет из-за плохого обеспечения — трибунал и понижение в звании.\n\n" +
                "<b>Отладка</b>\nF5 — сохранить, F9 — загрузить, F6 — ресурсы, F7 — волна, F8 — +100 ОР, F10 — смена погоды, P — визит высокого чина.";
            float h = UiSkin.Label.CalcHeight(new GUIContent(text), view.width - 24);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 20, h + 20));
            GUI.Label(new Rect(0, 0, view.width - 24, h), text, UiSkin.Label);
            GUI.EndScrollView();
        }

        // ------------------------------------------------------------ диалог

        void DrawDialogue()
        {
            var node = Game.Dialogue.Current;
            if (node == null) return;
            float w = 980f;
            float textH = UiSkin.Label.CalcHeight(new GUIContent(node.Text), w - 190);
            float h = Mathf.Min(H - 120, 76 + textH + node.Choices.Count * 36 + 30);
            h = Mathf.Max(h, 200f);
            var r = new Rect(W / 2 - w / 2, H - h - 40, w, h);
            UiSkin.Fill(new Rect(0, 0, W, H), new Color(0f, 0.02f, 0.04f, 0.4f));
            Block(new Rect(0, 0, W, H));
            UiSkin.Panel2(r);
            UiSkin.Brackets(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.6f), 14f, 2f);
            var pr = new Rect(r.x + 18, r.y + 16, 112, 128);
            Portrait(pr, node.Portrait, node.PortraitColor);
            GUI.Label(new Rect(r.x + 154, r.y + 12, w - 180, 24), node.Speaker.ToUpperInvariant(), UiSkin.Header);
            GUI.Label(new Rect(r.x + 154, r.y + 12, w - 180, 24), "СВЯЗЬ АКТИВНА", new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight, fontSize = 10 });
            UiSkin.Fill(new Rect(r.x + 154, r.y + 36, w - 180, 1), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.3f));
            GUI.Label(new Rect(r.x + 154, r.y + 46, w - 180, textH), node.Text, UiSkin.Label);
            float y = r.y + Mathf.Max(156f, 60 + textH);
            for (int i = 0; i < node.Choices.Count; i++)
            {
                var c = node.Choices[i];
                string why = c.Unavailable?.Invoke();
                string label = c.Text + (why != null ? UiSkin.Col($"  — {why}", UiSkin.Bad) : c.Hint != null ? UiSkin.Col($"  ({c.Hint})", UiSkin.Dim) : "");
                var br = new Rect(r.x + 154, y, w - 180, 30);
                var st = new GUIStyle(why != null ? UiSkin.ButtonDisabled : UiSkin.Button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(40, 10, 6, 6) };
                if (GUI.Button(br, label, st))
                {
                    Game.Dialogue.Choose(i);
                    if (!Game.Dialogue.IsOpen) _modalClosedFrame = Time.frameCount;
                }
                var key = new GUIStyle(UiSkin.Mono) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                key.normal.textColor = why != null ? UiSkin.Dim : UiSkin.Cyan;
                GUI.Label(new Rect(br.x + 6, br.y, 26, br.height), (i + 1).ToString(), key);
                y += 34;
            }
        }

        // ------------------------------------------------------------ пауза и конец игры

        void DrawPause()
        {
            UiSkin.Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.55f));
            var r = WindowRect(420, 470);
            Block(r);
            UiSkin.Window(r, "Пауза", "СИСТЕМА");
            float y = r.y + 62, x = r.x + 30, bw = r.width - 60;
            if (GUI.Button(new Rect(x, y, bw, 36), "Продолжить", UiSkin.ButtonAccent)) TogglePause();
            y += 44;
            if (GUI.Button(new Rect(x, y, bw, 36), "Сохранить [F5]", UiSkin.Button)) Game.Save.SaveGame();
            y += 44;
            if (GUI.Button(new Rect(x, y, bw, 36), Game.Save.HasSave ? "Загрузить [F9]" : "Загрузить (нет сохранения)", Game.Save.HasSave ? UiSkin.Button : UiSkin.ButtonDisabled) && Game.Save.HasSave) Game.Entry.LoadLatest();
            y += 44;
            if (GUI.Button(new Rect(x, y, bw, 36), "Новая игра", UiSkin.Button)) Game.Entry.Restart(false);
            y += 52;
            GUI.Label(new Rect(x, y, bw, 22), "Скорость игры", UiSkin.Small);
            y += 24;
            float[] speeds = { 1f, 2f, 3f };
            for (int i = 0; i < speeds.Length; i++)
                if (GUI.Button(new Rect(x + i * (bw / 3f), y, bw / 3f - 6, 28), $"×{speeds[i]:0}", Mathf.Approximately(_speed, speeds[i]) ? UiSkin.TabActive : UiSkin.Tab)) SetSpeed(speeds[i]);
            y += 40;
            GUI.Label(new Rect(x, y, bw, 22), $"Громкость: {Mathf.RoundToInt(Game.Sound.MasterVolume * 100)}%", UiSkin.Small);
            y += 22;
            Game.Sound.MasterVolume = GUI.HorizontalSlider(new Rect(x, y, bw, 20), Game.Sound.MasterVolume, 0f, 1f);
            y += 26;
            GUI.Label(new Rect(x, y, bw, 22), $"Масштаб интерфейса: {Game.Config.uiScale:0.00}", UiSkin.Small);
            y += 22;
            Game.Config.uiScale = GUI.HorizontalSlider(new Rect(x, y, bw, 20), Game.Config.uiScale, 0.6f, 1.6f);
        }

        void DrawGameOver()
        {
            UiSkin.Fill(new Rect(0, 0, W, H), new Color(0.1f, 0, 0, 0.6f));
            var r = WindowRect(560, 300);
            Block(r);
            UiSkin.Window(r, "Аванпост потерян", "СИГНАЛ ПОТЕРЯН");
            UiSkin.Brackets(new Rect(r.x - 6, r.y - 6, r.width + 12, r.height + 12), UiSkin.Bad, 18f, 2f);
            GUI.Label(new Rect(r.x + 30, r.y + 80, r.width - 60, 80),
                $"Реактор уничтожен на {Game.Clock.Day}-й день.\nОтражено волн: {Game.Onslaught.WavesRepelled}. Выполнено задач: {Game.Tasks.CompletedCount}.\nЗвание: {PlayerRankManager.RankName(Game.Rank.Current)}.",
                new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(new Rect(r.x + 40, r.yMax - 70, 220, 40), "Начать заново", UiSkin.ButtonAccent)) Game.Entry.Restart(false);
            if (Game.Save.HasSave && GUI.Button(new Rect(r.xMax - 260, r.yMax - 70, 220, 40), "Загрузить сохранение", UiSkin.Button)) Game.Entry.LoadLatest();
        }
    }
}
