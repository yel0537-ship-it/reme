using UnityEngine;

namespace Echo7
{
    public partial class UiManager
    {
        static readonly ResourceType[] HudResources =
        {
            ResourceType.Alloys, ResourceType.Ammo, ResourceType.Rations, ResourceType.Water,
            ResourceType.Titanium, ResourceType.Crystals, ResourceType.Silicon, ResourceType.Medkits, ResourceType.Credits
        };

        void DrawHud()
        {
            var rank = Game.Rank;
            var clock = Game.Clock;

            // --- ранг (слева сверху)
            var rr = new Rect(10, 10, 360, 92);
            Block(rr);
            GUI.Box(rr, GUIContent.none, UiSkin.Panel);
            GUI.Label(new Rect(22, 16, 340, 26), $"{UiSkin.Col(rank.Insignia, UiSkin.Accent)}  {PlayerRankManager.RankName(rank.Current)} {rank.PlayerName}", UiSkin.Header);
            int next = rank.PointsForNext;
            GUI.Label(new Rect(22, 42, 340, 20), next > 0 ? $"Очки ранга: {rank.Points} / {next}" : $"Очки ранга: {rank.Points} (максимум)", UiSkin.Small);
            UiSkin.Bar(new Rect(22, 64, 336, 6), rank.ProgressToNext, UiSkin.Accent);
            var repC = rank.Reputation >= 60 ? UiSkin.Good : rank.Reputation >= 30 ? UiSkin.Accent : UiSkin.Bad;
            GUI.Label(new Rect(22, 72, 340, 20), $"Репутация в Центре: {UiSkin.Col(rank.Reputation.ToString(), repC)}   ·   Навыки: {Game.Skills.Points} [K]", UiSkin.Small);

            // --- время, погода, шум (центр сверху)
            var cr = new Rect(W / 2 - 230, 10, 460, 74);
            Block(cr);
            GUI.Box(cr, GUIContent.none, UiSkin.Panel);
            string night = clock.IsNight ? UiSkin.Col(" · НОЧЬ", UiSkin.Cyan) : "";
            var wc = Game.Weather.Current == WeatherKind.Clear ? UiSkin.Text : UiSkin.Accent;
            GUI.Label(new Rect(cr.x + 14, cr.y + 6, 440, 24), $"<b>День {clock.Day}</b>  {clock.TimeString}{night}   ·   {UiSkin.Col(Game.Weather.Label, wc)}", new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleCenter, wordWrap = false });
            float noise = Game.Noise.Level;
            GUI.Label(new Rect(cr.x + 14, cr.y + 34, 60, 18), "Шум", UiSkin.Small);
            UiSkin.Bar(new Rect(cr.x + 70, cr.y + 40, 180, 7), noise / 60f, Color.Lerp(UiSkin.Good, UiSkin.Bad, noise / 60f));
            GUI.Label(new Rect(cr.x + 260, cr.y + 34, 60, 18), "Угроза", UiSkin.Small);
            var ons = Game.Onslaught;
            UiSkin.Bar(new Rect(cr.x + 320, cr.y + 40, 126, 7), ons.Pressure / ons.Threshold, new Color(0.8f, 0.3f, 0.6f));
            string waveLine;
            if (ons.Incoming != null) waveLine = UiSkin.Col($"Приближается: {ons.Incoming.Label} ({OnslaughtDirName(ons.Incoming)}) через {Mathf.Max(0, Mathf.RoundToInt(ons.Incoming.ArriveTime - Time.time))} с", UiSkin.Bad);
            else if (ons.AnyActive) waveLine = UiSkin.Col($"Идёт волна! Враждебных: {Game.Enemies.CountHostile()}", UiSkin.Bad);
            else waveLine = UiSkin.Col($"Поставка через ~{Mathf.Max(0f, Game.Supply.HoursToDelivery):0} ч", UiSkin.Dim);
            GUI.Label(new Rect(cr.x + 14, cr.y + 52, 440, 20), waveLine, new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false });

            // --- ресурсы (справа сверху)
            var bank = Game.Bank;
            var resR = new Rect(W - 380, 10, 370, 30 + 22 * Mathf.CeilToInt(HudResources.Length / 2f) + 26);
            Block(resR);
            GUI.Box(resR, GUIContent.none, UiSkin.Panel);
            GUI.Label(new Rect(resR.x + 12, resR.y + 6, 200, 20), "Склад", UiSkin.Header);
            GUI.Label(new Rect(resR.x + 160, resR.y + 8, 200, 20), $"вместимость {bank.Capacity}", new GUIStyle(UiSkin.Tiny) { alignment = TextAnchor.UpperRight });
            for (int i = 0; i < HudResources.Length; i++)
            {
                var t = HudResources[i];
                float x = resR.x + 12 + (i % 2) * 178;
                float y = resR.y + 30 + (i / 2) * 22;
                int v = bank[t];
                var vc = v <= 0 ? UiSkin.Bad : (t == ResourceType.Ammo && v < 60) || (t == ResourceType.Rations && v < 10) || (t == ResourceType.Water && v < 10) ? UiSkin.Accent : UiSkin.Text;
                GUI.Label(new Rect(x, y, 110, 20), UiSkin.Col(ResourceBank.Name(t), ResourceBank.ColorOf(t)), UiSkin.Small);
                GUI.Label(new Rect(x + 96, y, 70, 20), UiSkin.Col(v.ToString(), vc), new GUIStyle(UiSkin.Small) { alignment = TextAnchor.UpperRight });
            }
            var pw = Game.Power;
            var pc = pw.Balance >= 0 ? UiSkin.Good : UiSkin.Bad;
            GUI.Label(new Rect(resR.x + 12, resR.yMax - 26, 350, 20),
                $"Энергия: {UiSkin.Col((pw.Balance >= 0 ? "+" : "") + pw.Balance.ToString("0"), pc)}  ({pw.Production:0} / {pw.Demand:0})" + (pw.Satisfaction < 0.99f ? UiSkin.Col($"  дефицит {Mathf.RoundToInt((1 - pw.Satisfaction) * 100)}%", UiSkin.Bad) : ""), UiSkin.Small);

            DrawTaskTracker();
            DrawLogFeed(resR.yMax + 8);
            DrawOrbsAndHotbar();
            DrawBuildHint();
            DrawRadar();

            var pl = Game.Player;
            if (pl != null && pl.IsDead)
                GUI.Label(new Rect(W / 2 - 300, H / 2 - 60, 600, 60), $"Эвакуация через {Mathf.CeilToInt(pl.RespawnTimer)} с", UiSkin.Big);
        }

        static string OnslaughtDirName(OnslaughtWave w) => EnemyOnslaughtDirector.DirectionName(w.Direction);

        void DrawTaskTracker()
        {
            var tasks = Game.Tasks.Active;
            float h = 34 + tasks.Count * 46;
            var r = new Rect(10, 110, 360, h);
            Block(r);
            GUI.Box(r, GUIContent.none, UiSkin.Panel);
            GUI.Label(new Rect(r.x + 12, r.y + 6, 300, 22), "Задачи Центра [J]", UiSkin.Header);
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                float y = r.y + 32 + i * 46;
                string dl = t.DeadlineDay > 0 ? $"до дня {t.DeadlineDay}" : "";
                var dlc = t.DeadlineDay > 0 && t.DeadlineDay <= Game.Clock.Day ? UiSkin.Bad : UiSkin.Dim;
                GUI.Label(new Rect(r.x + 12, y, 240, 20), $"<b>{t.Title}</b>", UiSkin.Small);
                GUI.Label(new Rect(r.x + 230, y, 118, 20), UiSkin.Col(dl, dlc), new GUIStyle(UiSkin.Tiny) { alignment = TextAnchor.UpperRight });
                UiSkin.Bar(new Rect(r.x + 12, y + 22, 280, 6), t.Fraction, UiSkin.Cyan);
                GUI.Label(new Rect(r.x + 296, y + 15, 60, 20), $"{t.Progress}/{t.Target}", UiSkin.Tiny);
            }
        }

        void DrawLogFeed(float top)
        {
            var log = Game.Log;
            int n = Mathf.Min(7, log.Entries.Count);
            if (n == 0) return;
            var r = new Rect(W - 450, top, 440, 18 + n * 38);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.85f);
            GUI.Box(r, GUIContent.none, UiSkin.PanelLight);
            GUI.color = old;
            for (int i = 0; i < n; i++)
            {
                var e = log.Entries[log.Entries.Count - n + i];
                float age = Time.unscaledTime - e.Time;
                var c = MessageLog.ColorOf(e.Kind);
                c.a = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(age / 30f));
                var st = new GUIStyle(UiSkin.Tiny) { wordWrap = true };
                st.normal.textColor = c;
                GUI.Label(new Rect(r.x + 8, r.y + 6 + i * 38, r.width - 16, 38), $"[{e.Stamp}] {e.Text}", st);
            }
        }

        void DrawOrbsAndHotbar()
        {
            var pl = Game.Player;
            if (pl == null) return;
            float orb = 150f;
            var lo = new Rect(24, H - orb - 22, orb, orb);
            var ro = new Rect(W - orb - 24, H - orb - 22, orb, orb);
            Block(lo); Block(ro);
            UiSkin.Orb(lo, pl.Health.Fraction, UiSkin.OrbRed);
            GUI.Label(new Rect(lo.x, lo.y + orb * 0.38f, orb, 30), $"<b>{Mathf.CeilToInt(pl.Health.Current)}</b>", new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
            GUI.Label(new Rect(lo.x, lo.y + orb * 0.58f, orb, 20), pl.Sickness > 0f ? UiSkin.Col("лихорадка", UiSkin.Bad) : "здоровье", new GUIStyle(UiSkin.Tiny) { alignment = TextAnchor.MiddleCenter });
            float ammoFrac = Game.Bank[ResourceType.Ammo] / Mathf.Max(1f, Game.Bank.Capacity);
            UiSkin.Orb(ro, ammoFrac, UiSkin.OrbOrange);
            GUI.Label(new Rect(ro.x, ro.y + orb * 0.38f, orb, 30), $"<b>{Game.Bank[ResourceType.Ammo]}</b>", new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
            GUI.Label(new Rect(ro.x, ro.y + orb * 0.58f, orb, 20), "патроны", new GUIStyle(UiSkin.Tiny) { alignment = TextAnchor.MiddleCenter });

            // способности 1–4
            float slot = 66f, gap = 8f;
            float total = 4 * slot + 3 * gap;
            float x0 = W / 2 - total / 2, y0 = H - slot - 58;
            var bar = new Rect(x0 - 14, y0 - 10, total + 28, slot + 64);
            Block(bar);
            GUI.Box(bar, GUIContent.none, UiSkin.Panel);
            for (int i = 0; i < 4; i++)
            {
                var a = pl.Abilities[i];
                var r = new Rect(x0 + i * (slot + gap), y0, slot, slot);
                bool affordable = a.Cost == null || Game.Bank.CanAfford(a.Cost);
                if (GUI.Button(r, new GUIContent($"<b>{i + 1}</b>\n{a.Name}", $"{a.Name}: {a.Description}\nЦена: {(a.Cost != null ? a.Cost.ToString() : "—")} · перезарядка {a.Cooldown:0} с"), affordable ? UiSkin.Button : UiSkin.ButtonDisabled))
                    pl.UseAbility(i);
                if (a.CooldownLeft > 0f) UiSkin.Fill(new Rect(r.x, r.y + r.height * (1f - a.CooldownFraction), r.width, r.height * a.CooldownFraction), new Color(0, 0, 0, 0.6f));
            }
            // кнопки окон
            string[] labels = { "Стройка [B]", "Навыки [K]", "Задачи [J]", "Гарнизон [G]", "Карта [M]", "? [F1]" };
            UiWindow[] wins = { UiWindow.Build, UiWindow.Skills, UiWindow.Tasks, UiWindow.Garrison, UiWindow.GlobalMap, UiWindow.Help };
            float bw = (total + 4) / 3f;
            for (int i = 0; i < labels.Length; i++)
            {
                var br = new Rect(x0 - 2 + (i % 3) * bw, y0 + slot + 6 + (i / 3) * 22, bw - 4, 20);
                var lockF = RequiredFeature(wins[i]);
                bool locked = lockF.HasValue && !Game.Rank.IsUnlocked(lockF.Value);
                if (GUI.Button(br, labels[i], Window == wins[i] ? UiSkin.TabActive : locked ? UiSkin.ButtonDisabled : UiSkin.Tab)) Open(wins[i]);
            }
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                var tr = new Rect(x0, y0 - 70, total, 58);
                GUI.Box(tr, GUIContent.none, UiSkin.PanelLight);
                GUI.Label(new Rect(tr.x + 8, tr.y + 4, tr.width - 16, tr.height - 8), GUI.tooltip, UiSkin.Small);
            }
        }

        void DrawBuildHint()
        {
            var b = Game.Build;
            if (b == null || !b.IsPlacing) return;
            var d = b.Selected;
            var r = new Rect(W / 2 - 330, H - 230, 660, 64);
            Block(r);
            GUI.Box(r, GUIContent.none, UiSkin.Panel);
            GUI.Label(new Rect(r.x + 12, r.y + 6, 640, 22), $"<b>{d.Name}</b>  ·  {b.CostOf(d)}  ·  R — поворот  ·  ПКМ/Esc — отмена{(d.LinePlacement ? "  ·  тяните мышью для линии" : "  ·  Shift — несколько")}", UiSkin.Small);
            string why = b.LastInvalidReason;
            string ok = b.OutOfPower ? UiSkin.Col("Место подходит, но вне энергосети — поставьте энергоузел рядом", UiSkin.Accent) : UiSkin.Col("Место подходит", UiSkin.Good);
            GUI.Label(new Rect(r.x + 12, r.y + 32, 640, 22), why == null ? ok : UiSkin.Col(why, UiSkin.Bad), UiSkin.Small);
        }

        /// <summary>Радар: база, постройки, враги, гарнизон в радиусе 70 м.</summary>
        void DrawRadar()
        {
            var pl = Game.Player;
            if (pl == null) return;
            float size = 170f;
            var r = new Rect(W - size - 24, H - 150 - 22 - size - 16, size, size);
            Block(r);
            GUI.DrawTexture(r, UiSkin.OrbBack);
            GUI.DrawTexture(r, UiSkin.RingTex);
            var c = pl.transform.position;
            float range = 70f;
            float yaw = Game.CameraRig != null ? Game.CameraRig.Yaw : 0f;
            var rot = Quaternion.Euler(0, -yaw, 0);
            void Dot(Vector3 world, Color col, float s)
            {
                var d = rot * (world - c);
                var v = new Vector2(d.x, -d.z) / range * (size * 0.5f - 6f);
                if (v.magnitude > size * 0.5f - 6f) return;
                var old = GUI.color; GUI.color = col;
                GUI.DrawTexture(new Rect(r.center.x + v.x - s / 2, r.center.y + v.y - s / 2, s, s), UiSkin.Dot);
                GUI.color = old;
            }
            foreach (var s in Registry.Structures) if (s != null && !s.IsDestroyed) Dot(s.transform.position, s.IsTargetCore ? UiSkin.Accent : new Color(0.7f, 0.72f, 0.75f, 0.8f), s.IsTargetCore ? 10f : 5f);
            foreach (var d in Registry.Deposits) if (d != null && !d.Hidden) Dot(d.transform.position, ResourceBank.ColorOf(d.Type), 5f);
            foreach (var e in Registry.Enemies) if (e != null && !e.IsDead) Dot(e.transform.position, e.IsHostile ? UiSkin.Bad : new Color(0.9f, 0.8f, 0.4f, 0.7f), e.Archetype.Kind == EnemyKind.Tank ? 10f : 5f);
            foreach (var n in Registry.Npcs) if (n != null && !n.IsAway) Dot(n.transform.position, UiSkin.Good, 5f);
            Dot(c, Color.white, 8f);
        }
    }
}
