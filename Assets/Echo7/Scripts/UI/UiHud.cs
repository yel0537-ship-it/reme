using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// HUD в хайтек-стиле: командная панель, статус-строка (время/погода/шум/угроза), склад, журнал-терминал,
    /// биомонитор костюма (ЭКГ + сегментная шкала) вместо «шара здоровья», панель вооружения вместо «шара маны»,
    /// шестигранные слоты способностей, голографический радар. Части HUD открываются по ходу обучения (HudPart).
    /// </summary>
    public partial class UiManager
    {
        static readonly ResourceType[] HudResources =
        {
            ResourceType.Alloys, ResourceType.Ammo, ResourceType.Rations, ResourceType.Water,
            ResourceType.Titanium, ResourceType.Crystals, ResourceType.Silicon, ResourceType.Medkits, ResourceType.Credits
        };

        static readonly string[] AbilityCodes = { "ГРН", "ДРН", "МЕД", "ЗРД" };

        const float BottomPanelH = 134f;
        float _leftColumnBottom = 120f;   // низ левой колонки (цели/подсказки) — сюда встаёт окно стройки
        float _skipConfirmUntil;

        static bool Show(HudPart p) => Game.Tutorial == null || Game.Tutorial.Shows(p);

        void DrawHud()
        {
            if (Game.Tutorial != null && Game.Tutorial.ModeSelectOpen) return;
            float leftY = 12f;
            if (Show(HudPart.Rank)) { DrawCommandPanel(new Rect(12, 12, 380, 96)); leftY = 118f; }
            DrawStatusStrip();
            float rightY = 12f;
            if (Show(HudPart.Resources)) rightY = DrawResources(12f) + 8f;
            if (Show(HudPart.Log)) DrawLogFeed(rightY);

            if (Show(HudPart.Objectives)) leftY = DrawObjectives(leftY) + 8f;
            leftY = DrawTipCard(leftY);
            _leftColumnBottom = leftY;

            if (Show(HudPart.Bio)) DrawBioMonitor(new Rect(16, H - BottomPanelH - 16, 420, BottomPanelH));
            if (Show(HudPart.Weapon)) DrawWeaponPanel(new Rect(W - 436, H - BottomPanelH - 16, 420, BottomPanelH));
            DrawConsole();
            DrawBuildHint();
            if (Show(HudPart.Radar)) DrawRadar();
            DrawMarkerPointer();

            var pl = Game.Player;
            if (pl != null && pl.IsDead)
            {
                UiSkin.Fill(new Rect(0, H / 2 - 50, W, 100), new Color(0.2f, 0.02f, 0.02f, 0.45f));
                GUI.Label(new Rect(W / 2 - 400, H / 2 - 40, 800, 40), "СИГНАЛ ЖИЗНЕОБЕСПЕЧЕНИЯ ПОТЕРЯН", new GUIStyle(UiSkin.Big) { fontSize = 28 });
                GUI.Label(new Rect(W / 2 - 300, H / 2 + 4, 600, 30), $"эвакуация на базу через {Mathf.CeilToInt(pl.RespawnTimer)} с", new GUIStyle(UiSkin.Mono) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
            }
        }

        static string OnslaughtDirName(OnslaughtWave w) => EnemyOnslaughtDirector.DirectionName(w.Direction);

        // ------------------------------------------------------------ командная панель (ранг)

        void DrawCommandPanel(Rect r)
        {
            var rank = Game.Rank;
            Block(r);
            UiSkin.Panel2(r);
            // шеврон звания в шестиграннике
            var hex = new Rect(r.x + 10, r.y + 12, 62, 70);
            UiSkin.Hex(hex, new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.18f), true);
            UiSkin.Hex(hex, UiSkin.Accent, false);
            var ins = new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, wordWrap = false };
            ins.normal.textColor = UiSkin.Accent;
            GUI.Label(hex, rank.Insignia, ins);

            float x = r.x + 84, w = r.width - 96;
            GUI.Label(new Rect(x, r.y + 8, w, 22), $"{PlayerRankManager.RankName(rank.Current).ToUpperInvariant()} {rank.PlayerName.ToUpperInvariant()}", UiSkin.Header);
            GUI.Label(new Rect(x, r.y + 9, w, 20), "ЭХО-7 / КОМАНДОВАНИЕ", new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight, fontSize = 10 });
            int next = rank.PointsForNext;
            GUI.Label(new Rect(x, r.y + 30, w, 18), next > 0 ? $"ОР {rank.Points:0000} / {next:0000}" : $"ОР {rank.Points:0000} · МАКС", UiSkin.MonoSmall);
            UiSkin.SegBar(new Rect(x, r.y + 48, w, 6), rank.ProgressToNext, 24, UiSkin.Accent, 2f);
            var repC = rank.Reputation >= 60 ? UiSkin.Good : rank.Reputation >= 30 ? UiSkin.Accent : UiSkin.Bad;
            GUI.Label(new Rect(x, r.y + 60, 90, 18), "РЕПУТАЦИЯ", UiSkin.MonoSmall);
            UiSkin.SegBar(new Rect(x + 84, r.y + 65, 120, 6), rank.Reputation / 100f, 10, repC, 2f);
            GUI.Label(new Rect(x + 210, r.y + 60, 40, 18), UiSkin.Col(rank.Reputation.ToString(), repC), UiSkin.MonoSmall);
            int sp = Game.Skills.Points;
            var spStyle = new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight, richText = true };
            string spText = sp > 0 ? UiSkin.Col($"НАВЫКИ +{sp} [K]", Color.Lerp(UiSkin.Accent, Color.white, UiSkin.Pulse(5f) * 0.5f)) : "НАВЫКИ [K]";
            GUI.Label(new Rect(x, r.y + 76, w, 18), spText, spStyle);
        }

        // ------------------------------------------------------------ статус-строка

        void DrawStatusStrip()
        {
            bool clock = Show(HudPart.Clock), threat = Show(HudPart.Threat);
            if (!clock && !threat) return;
            float h = 12f + (clock ? 26f : 0f) + (threat ? 22f : 0f) + (threat ? 20f : 0f);
            var r = new Rect(W / 2 - 270, 12, 540, h);
            Block(r);
            UiSkin.Panel2(r);
            float y = r.y + 6;
            var c = Game.Clock;
            if (clock)
            {
                var mono = new GUIStyle(UiSkin.Mono) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                mono.normal.textColor = UiSkin.Cyan;
                GUI.Label(new Rect(r.x + 14, y, 200, 24), $"Д{c.Day:00} · {c.TimeString}", mono);
                string right;
                if (c.Frozen) right = UiSkin.Col("ВРЕМЯ ОСТАНОВЛЕНО · ОБУЧЕНИЕ", Color.Lerp(UiSkin.Accent, Color.white, UiSkin.Pulse(2f) * 0.3f));
                else
                {
                    var wc = Game.Weather.Current == WeatherKind.Clear ? UiSkin.Text : UiSkin.Accent;
                    right = (c.IsNight ? UiSkin.Col("НОЧЬ  ·  ", UiSkin.Cyan) : UiSkin.Col("ДЕНЬ  ·  ", UiSkin.Dim)) + UiSkin.Col(Game.Weather.Label.ToUpperInvariant(), wc);
                    if (Mathf.Abs(Time.timeScale - 1f) > 0.01f && Time.timeScale > 0f) right += UiSkin.Col($"  ·  ×{Time.timeScale:0}", UiSkin.Accent);
                }
                GUI.Label(new Rect(r.x + 200, y + 2, r.width - 214, 22), right, new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleRight, wordWrap = false });
                y += 26;
            }
            if (threat)
            {
                float noise = Game.Noise.Level;
                var ons = Game.Onslaught;
                float half = (r.width - 28) / 2f;
                GUI.Label(new Rect(r.x + 14, y, 50, 18), "ШУМ", UiSkin.MonoSmall);
                UiSkin.SegBar(new Rect(r.x + 58, y + 5, half - 60, 8), noise / 60f, 16, Color.Lerp(UiSkin.Good, UiSkin.Bad, noise / 60f), 2f);
                float tx = r.x + 14 + half + 8;
                GUI.Label(new Rect(tx, y, 70, 18), "УГРОЗА", UiSkin.MonoSmall);
                float pf = ons.Threshold > 0f ? ons.Pressure / ons.Threshold : 0f;
                UiSkin.SegBar(new Rect(tx + 62, y + 5, half - 70, 8), pf, 16, Color.Lerp(new Color(0.75f, 0.35f, 1f), UiSkin.Bad, pf), 2f);
                y += 22;
                string waveLine;
                if (ons.Incoming != null) waveLine = UiSkin.Col($"▲ КОНТАКТ: {ons.Incoming.Label} · {OnslaughtDirName(ons.Incoming)} · {Mathf.Max(0, Mathf.RoundToInt(ons.Incoming.ArriveTime - Time.time))} с", Color.Lerp(UiSkin.Bad, Color.white, UiSkin.Pulse(6f) * 0.3f));
                else if (ons.AnyActive) waveLine = UiSkin.Col($"▲ ВОЛНА · ВРАЖДЕБНЫХ: {Game.Enemies.CountHostile()}", UiSkin.Bad);
                else if (Game.Tutorial != null && Game.Tutorial.Active && !Game.Tutorial.AllowNightWaves) waveLine = UiSkin.Col("СЕЙСМОДАТЧИКИ: ТИХО", UiSkin.Dim);
                else waveLine = UiSkin.Col($"ПОСТАВКА ЧЕРЕЗ ~{Mathf.Max(0f, Game.Supply.HoursToDelivery):0} Ч", UiSkin.Dim);
                GUI.Label(new Rect(r.x + 14, y, r.width - 28, 18), waveLine, new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.MiddleCenter, richText = true });
            }
        }

        // ------------------------------------------------------------ склад

        float DrawResources(float top)
        {
            var bank = Game.Bank;
            bool energy = Show(HudPart.Energy);
            int rows = Mathf.CeilToInt(HudResources.Length / 2f);
            var r = new Rect(W - 392, top, 380, 34 + 21 * rows + (energy ? 30 : 6));
            Block(r);
            UiSkin.Panel2(r);
            GUI.Label(new Rect(r.x + 14, r.y + 8, 200, 20), "СКЛАД", UiSkin.Header);
            GUI.Label(new Rect(r.x + 160, r.y + 9, 206, 18), $"ЁМК {bank.Capacity}", new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight });
            var valStyle = new GUIStyle(UiSkin.Mono) { alignment = TextAnchor.UpperRight, richText = true };
            for (int i = 0; i < HudResources.Length; i++)
            {
                var t = HudResources[i];
                float x = r.x + 14 + (i % 2) * 180;
                float y = r.y + 32 + (i / 2) * 21;
                int v = bank[t];
                bool low = (t == ResourceType.Ammo && v < 60) || (t == ResourceType.Rations && v < 10) || (t == ResourceType.Water && v < 10);
                var vc = v <= 0 ? UiSkin.Bad : low ? Color.Lerp(UiSkin.Accent, Color.white, UiSkin.Pulse(4f) * 0.4f) : UiSkin.Text;
                UiSkin.Fill(new Rect(x, y + 6, 7, 7), ResourceBank.ColorOf(t));
                GUI.Label(new Rect(x + 13, y, 110, 20), ResourceBank.Name(t), new GUIStyle(UiSkin.Small) { wordWrap = false });
                GUI.Label(new Rect(x + 96, y, 70, 20), UiSkin.Col(v.ToString(), vc), valStyle);
            }
            if (energy)
            {
                var pw = Game.Power;
                float y = r.yMax - 28;
                UiSkin.Fill(new Rect(r.x + 12, y - 2, r.width - 24, 1), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.25f));
                var pc = pw.Balance >= 0 ? UiSkin.Good : UiSkin.Bad;
                GUI.Label(new Rect(r.x + 14, y + 2, 80, 20), "ЭНЕРГИЯ", UiSkin.MonoSmall);
                float sat = pw.Demand > 0f ? Mathf.Clamp01(pw.Production / pw.Demand) : 1f;
                UiSkin.SegBar(new Rect(r.x + 88, y + 8, 120, 7), sat, 12, pc, 2f);
                string txt = $"{(pw.Balance >= 0 ? "+" : "")}{pw.Balance:0}  ({pw.Production:0}/{pw.Demand:0})";
                if (pw.Satisfaction < 0.99f) txt += $"  −{Mathf.RoundToInt((1 - pw.Satisfaction) * 100)}%";
                GUI.Label(new Rect(r.x + 214, y + 2, r.width - 228, 20), UiSkin.Col(txt, pc), new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight, richText = true });
            }
            return r.yMax;
        }

        // ------------------------------------------------------------ журнал-терминал

        void DrawLogFeed(float top)
        {
            var log = Game.Log;
            int n = Mathf.Min(6, log.Entries.Count);
            if (n == 0) return;
            float lineH = 34f;
            var r = new Rect(W - 452, top, 440, 26 + n * lineH);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.8f);
            GUI.Box(r, GUIContent.none, UiSkin.PanelLight);
            GUI.color = old;
            GUI.Label(new Rect(r.x + 10, r.y + 4, 200, 16), "ЖУРНАЛ СВЯЗИ", new GUIStyle(UiSkin.MonoSmall) { fontSize = 10 });
            UiSkin.Fill(new Rect(r.x + 10, r.y + 20, r.width - 20, 1), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.2f));
            var st = new GUIStyle(UiSkin.Tiny) { wordWrap = true, richText = true };
            for (int i = 0; i < n; i++)
            {
                var e = log.Entries[log.Entries.Count - n + i];
                float age = Time.unscaledTime - e.Time;
                var c = MessageLog.ColorOf(e.Kind);
                c.a = Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(age / 30f));
                st.normal.textColor = c;
                var dim = UiSkin.Dim; dim.a = c.a;
                GUI.Label(new Rect(r.x + 10, r.y + 24 + i * lineH, r.width - 20, lineH), $"{UiSkin.Col(e.Stamp, dim)} › {e.Text}", st);
            }
        }

        // ------------------------------------------------------------ цели: обучение или задачи Центра

        float DrawObjectives(float top)
        {
            var tut = Game.Tutorial;
            if (tut != null && tut.Active && tut.Current != null) return DrawTutorialObjective(top, tut);

            var tasks = Game.Tasks.Active;
            if (tasks.Count == 0) return top - 8f;
            float h = 36 + tasks.Count * 46;
            var r = new Rect(12, top, 380, h);
            Block(r);
            UiSkin.Panel2(r);
            GUI.Label(new Rect(r.x + 14, r.y + 8, 300, 20), "ЗАДАЧИ ЦЕНТРА", UiSkin.Header);
            GUI.Label(new Rect(r.x + 14, r.y + 9, r.width - 28, 18), "[J]", new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight });
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                float y = r.y + 34 + i * 46;
                string dl = t.DeadlineDay > 0 ? $"ДО Д{t.DeadlineDay:00}" : "";
                var dlc = t.DeadlineDay > 0 && t.DeadlineDay <= Game.Clock.Day ? UiSkin.Bad : UiSkin.Dim;
                UiSkin.Tex(new Rect(r.x + 14, y + 5, 8, 8), UiSkin.Diamond, UiSkin.Cyan);
                GUI.Label(new Rect(r.x + 28, y, 250, 20), t.Title, new GUIStyle(UiSkin.Small) { wordWrap = false, fontStyle = FontStyle.Bold });
                GUI.Label(new Rect(r.x + 240, y + 1, 126, 18), UiSkin.Col(dl, dlc), new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight, richText = true });
                UiSkin.SegBar(new Rect(r.x + 28, y + 24, 260, 6), t.Fraction, 20, UiSkin.Cyan, 2f);
                GUI.Label(new Rect(r.x + 294, y + 17, 72, 18), $"{t.Progress}/{t.Target}", new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight });
            }
            return r.yMax;
        }

        float DrawTutorialObjective(float top, TutorialDirector tut)
        {
            var s = tut.Current;
            float w = 380f;
            var objStyle = new GUIStyle(UiSkin.Label) { fontSize = 15 };
            float objH = objStyle.CalcHeight(new GUIContent(s.Objective), w - 42);
            float hintH = string.IsNullOrEmpty(s.Hint) ? 0f : 18f;
            float h = 34 + 24 + objH + 8 + 12 + hintH + 8 + 30 + 10;
            var r = new Rect(12, top, w, h);
            Block(r);
            UiSkin.Panel2(r);
            float p = s.Progress();
            bool done = p >= 1f;
            UiSkin.Fill(new Rect(r.x + 3, r.y + 8, 3, r.height - 16), done ? UiSkin.Good : UiSkin.Cyan);
            GUI.Label(new Rect(r.x + 14, r.y + 8, 260, 20), "ОБУЧЕНИЕ", UiSkin.Header);
            GUI.Label(new Rect(r.x + 14, r.y + 9, r.width - 28, 18), $"{tut.StepIndex + 1:00}/{tut.StepCount:00}", new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight });
            // шкала прохождения пролога
            UiSkin.SegBar(new Rect(r.x + 110, r.y + 15, 180, 4), (tut.StepIndex + (done ? 1f : 0f)) / tut.StepCount, tut.StepCount, UiSkin.CyanDim, 3f);
            float y = r.y + 32;
            GUI.Label(new Rect(r.x + 14, y, w - 28, 24), s.Title.ToUpperInvariant(), new GUIStyle(UiSkin.Label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = false });
            y += 24;
            UiSkin.Tex(new Rect(r.x + 16, y + 6, 9, 9), UiSkin.Diamond, done ? UiSkin.Good : Color.Lerp(UiSkin.Cyan, Color.white, UiSkin.Pulse(3f) * 0.4f));
            GUI.Label(new Rect(r.x + 32, y, w - 46, objH), done ? UiSkin.Col(s.Objective, UiSkin.Good) : s.Objective, objStyle);
            y += objH + 8;
            UiSkin.SegBar(new Rect(r.x + 32, y, 260, 7), p, 20, done ? UiSkin.Good : UiSkin.Cyan, 2f);
            string pt = done ? "ГОТОВО" : s.ProgressText != null ? s.ProgressText() : $"{Mathf.RoundToInt(p * 100)}%";
            GUI.Label(new Rect(r.x + 296, y - 6, 70, 18), UiSkin.Col(pt, done ? UiSkin.Good : UiSkin.Text), new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight, richText = true });
            y += 12;
            if (hintH > 0f)
            {
                GUI.Label(new Rect(r.x + 32, y, w - 46, 18), UiSkin.Col("› " + s.Hint, UiSkin.Accent), new GUIStyle(UiSkin.Tiny) { wordWrap = false });
                y += hintH;
            }
            y += 8;
            float bw = (w - 36) / 2f;
            if (GUI.Button(new Rect(r.x + 14, y, bw, 26), "Брифинг", UiSkin.Tab)) tut.ReopenBriefing();
            bool confirm = Time.unscaledTime < _skipConfirmUntil;
            if (GUI.Button(new Rect(r.x + 22 + bw, y, bw, 26), confirm ? UiSkin.Col("Точно пропустить?", UiSkin.Bad) : "Пропустить обучение", UiSkin.Tab))
            {
                if (confirm) { _skipConfirmUntil = 0f; tut.Skip(); }
                else _skipConfirmUntil = Time.unscaledTime + 3f;
            }
            return r.yMax;
        }

        float DrawTipCard(float top)
        {
            var tut = Game.Tutorial;
            var tip = tut?.Tip;
            if (tip == null) return top;
            float w = 380f;
            float textH = UiSkin.Small.CalcHeight(new GUIContent(tip.Text), w - 34);
            var r = new Rect(12, top, w, 36 + textH + 38);
            float age = Time.unscaledTime - tip.Shown;
            float slide = Mathf.Clamp01(age * 4f);
            r.x -= (1f - slide) * 60f;
            Block(r);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, slide);
            UiSkin.Panel2(r);
            UiSkin.Fill(new Rect(r.x + 3, r.y + 8, 3, r.height - 16), UiSkin.Accent);
            GUI.Label(new Rect(r.x + 14, r.y + 8, w - 60, 20), UiSkin.Col("НОВОЕ · ", UiSkin.Accent) + tip.Title.ToUpperInvariant(), new GUIStyle(UiSkin.Header) { richText = true });
            if (GUI.Button(new Rect(r.xMax - 34, r.y + 6, 26, 22), "✕", UiSkin.Tab)) tut.DismissTip();
            GUI.Label(new Rect(r.x + 14, r.y + 32, w - 34, textH), tip.Text, UiSkin.Small);
            // таймер автозакрытия
            UiSkin.Fill(new Rect(r.x + 14, r.yMax - 8, (w - 28) * Mathf.Clamp01(1f - age / 30f), 2), new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.6f));
            if (GUI.Button(new Rect(r.xMax - 124, r.yMax - 34, 110, 22), "Понятно", UiSkin.Tab)) tut.DismissTip();
            GUI.color = old;
            return r.yMax + 8f;
        }

        // ------------------------------------------------------------ биомонитор (вместо шара здоровья)

        void DrawBioMonitor(Rect r)
        {
            var pl = Game.Player;
            if (pl == null) return;
            Block(r);
            UiSkin.Panel2(r);
            float f = pl.Health.Fraction;
            var hc = UiSkin.HealthColor(f);
            string status; Color sc;
            if (pl.IsDead) { status = "ЭВАКУАЦИЯ"; sc = UiSkin.Bad; }
            else if (pl.Sickness > 0f) { status = "ЛИХОРАДКА"; sc = UiSkin.Accent; }
            else if (f < 0.3f) { status = "КРИТИЧНО"; sc = Color.Lerp(UiSkin.Bad, Color.white, UiSkin.Pulse(8f) * 0.5f); }
            else if (f < 0.6f) { status = "РАНЕН"; sc = UiSkin.Accent; }
            else { status = "НОРМА"; sc = UiSkin.Good; }

            GUI.Label(new Rect(r.x + 14, r.y + 8, 200, 20), "БИОМОНИТОР", UiSkin.Header);
            GUI.Label(new Rect(r.x + 14, r.y + 8, r.width - 28, 20), UiSkin.Col("● ", sc) + UiSkin.Col(status, sc), new GUIStyle(UiSkin.Mono) { alignment = TextAnchor.UpperRight, richText = true, fontStyle = FontStyle.Bold });

            var big = new GUIStyle(UiSkin.MonoBig);
            big.normal.textColor = hc;
            GUI.Label(new Rect(r.x + 12, r.y + 30, 120, 44), Mathf.CeilToInt(pl.Health.Current).ToString("000"), big);
            GUI.Label(new Rect(r.x + 16, r.y + 70, 120, 16), $"/ {Mathf.CeilToInt(pl.Health.Max)} ЦЕЛОСТН.", new GUIStyle(UiSkin.MonoSmall) { fontSize = 10 });

            // ЭКГ: частота растёт при ранении, на нуле — прямая линия
            var ecg = new Rect(r.x + 138, r.y + 32, r.width - 152, 48);
            UiSkin.Fill(ecg, new Color(0f, 0.04f, 0.06f, 0.6f));
            for (int i = 1; i < 6; i++) UiSkin.Fill(new Rect(ecg.x + ecg.width * i / 6f, ecg.y, 1, ecg.height), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.07f));
            UiSkin.Fill(new Rect(ecg.x, ecg.center.y, ecg.width, 1), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.1f));
            DrawEcg(ecg, pl.IsDead ? 0f : 1.1f + (1f - f) * 1.8f, hc, pl.IsDead);

            UiSkin.SegBar(new Rect(r.x + 14, r.y + 90, r.width - 28, 12), f, 30, hc, 2f);
            int med = Game.Bank[ResourceType.Medkits];
            string line = $"КОСТЮМ ВК-3  ·  АПТЕЧКИ {med} [3]";
            if (pl.Sickness > 0f) line += UiSkin.Col($"  ·  ЛИХОРАДКА {Mathf.RoundToInt(pl.Sickness * 100)}%", UiSkin.Accent);
            GUI.Label(new Rect(r.x + 14, r.y + 106, r.width - 28, 18), line, new GUIStyle(UiSkin.MonoSmall) { richText = true });
            UiSkin.Brackets(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(hc.r, hc.g, hc.b, 0.45f), 12f, 2f);
        }

        static float EcgShape(float ph)
        {
            // P — небольшой бугорок, QRS — резкий пик, T — широкая волна
            float v = 0f;
            v += 0.12f * Mathf.Exp(-Mathf.Pow((ph - 0.12f) / 0.035f, 2f));
            v -= 0.15f * Mathf.Exp(-Mathf.Pow((ph - 0.27f) / 0.010f, 2f));
            v += 1.00f * Mathf.Exp(-Mathf.Pow((ph - 0.30f) / 0.012f, 2f));
            v -= 0.28f * Mathf.Exp(-Mathf.Pow((ph - 0.33f) / 0.012f, 2f));
            v += 0.25f * Mathf.Exp(-Mathf.Pow((ph - 0.55f) / 0.06f, 2f));
            return v;
        }

        static void DrawEcg(Rect r, float beatsPerSecond, Color c, bool flat)
        {
            int cols = Mathf.FloorToInt(r.width / 2f);
            float window = 2.4f;                     // секунд на экран
            float now = Time.unscaledTime;
            float prev = 0f;
            for (int i = 0; i < cols; i++)
            {
                float t = now - window * (1f - (float)i / cols);
                float v = flat ? 0f : EcgShape(Mathf.Repeat(t * beatsPerSecond, 1f));
                float y = r.center.y - v * r.height * 0.42f;
                if (i == 0) prev = y;
                float y0 = Mathf.Min(prev, y), y1 = Mathf.Max(prev, y);
                float fade = (float)i / cols;
                var col = new Color(c.r, c.g, c.b, 0.25f + 0.75f * fade);
                UiSkin.Fill(new Rect(r.x + i * 2f, y0 - 1f, 2f, y1 - y0 + 2f), col);
                prev = y;
            }
            // «головка» самописца
            UiSkin.Tex(new Rect(r.xMax - 5, prev - 4, 8, 8), UiSkin.Dot, Color.white);
        }

        // ------------------------------------------------------------ вооружение (вместо шара маны)

        void DrawWeaponPanel(Rect r)
        {
            var pl = Game.Player;
            if (pl == null) return;
            Block(r);
            UiSkin.Panel2(r);
            int ammo = Game.Bank[ResourceType.Ammo];
            int cap = Mathf.Max(1, Game.Bank.CapOf(ResourceType.Ammo));
            bool low = ammo < 60;
            var ac = ammo <= 0 ? UiSkin.Bad : low ? Color.Lerp(UiSkin.Accent, UiSkin.Bad, UiSkin.Pulse(5f)) : UiSkin.Cyan;
            GUI.Label(new Rect(r.x + 14, r.y + 8, 200, 20), "ВООРУЖЕНИЕ", UiSkin.Header);
            GUI.Label(new Rect(r.x + 14, r.y + 9, r.width - 28, 20), low ? UiSkin.Col(ammo <= 0 ? "НЕТ БОЕПРИПАСОВ" : "НИЗКИЙ БОЕЗАПАС", ac) : "ИК-7 · ИМПУЛЬСНЫЙ КАРАБИН", new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperRight, richText = true });
            var big = new GUIStyle(UiSkin.MonoBig);
            big.normal.textColor = ac;
            GUI.Label(new Rect(r.x + 12, r.y + 30, 140, 44), ammo.ToString("000"), big);
            GUI.Label(new Rect(r.x + 16, r.y + 70, 140, 16), $"/ {cap} ПАТР.", new GUIStyle(UiSkin.MonoSmall) { fontSize = 10 });

            // кассета: 10 столбиков, каждый — 10% ёмкости склада
            var mag = new Rect(r.x + 150, r.y + 34, r.width - 164, 44);
            float frac = Mathf.Clamp01((float)ammo / cap);
            int cells = 20;
            float cw = (mag.width - (cells - 1) * 3f) / cells;
            int lit = Mathf.CeilToInt(frac * cells - 0.001f);
            for (int i = 0; i < cells; i++)
            {
                var cr = new Rect(mag.x + i * (cw + 3f), mag.y, cw, mag.height);
                bool on = i < lit;
                UiSkin.Fill(cr, on ? new Color(ac.r, ac.g, ac.b, 0.85f) : new Color(ac.r * 0.2f, ac.g * 0.2f, ac.b * 0.2f, 0.5f));
                if (on) UiSkin.Fill(new Rect(cr.x, cr.y, cr.width, 3), Color.white * new Color(1, 1, 1, 0.5f));
            }
            UiSkin.Fill(new Rect(r.x + 14, r.y + 90, r.width - 28, 1), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.25f));
            string mode = pl.AttackTarget != null && !pl.AttackTarget.IsDead ? UiSkin.Col("ЦЕЛЬ ЗАХВАЧЕНА", UiSkin.Bad) : "РЕЖИМ: ОДИНОЧНЫЙ";
            GUI.Label(new Rect(r.x + 14, r.y + 96, r.width - 28, 18), $"{mode}  ·  ДАЛЬН {pl.WeaponRange:0} М  ·  ПКМ — ОГОНЬ В ТОЧКУ", new GUIStyle(UiSkin.MonoSmall) { richText = true });
            GUI.Label(new Rect(r.x + 14, r.y + 112, r.width - 28, 18), $"СПЛАВЫ {Game.Bank[ResourceType.Alloys]}  ·  ГРАНАТА 4 ПАТР + 1 СПЛ", new GUIStyle(UiSkin.MonoSmall) { fontSize = 10 });
            UiSkin.Brackets(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(ac.r, ac.g, ac.b, 0.45f), 12f, 2f);
        }

        // ------------------------------------------------------------ консоль: способности и меню

        void DrawConsole()
        {
            var pl = Game.Player;
            if (pl == null) return;
            var tut = Game.Tutorial;
            float slot = 74f, gap = 16f;
            float total = 4 * slot + 3 * gap;
            float x0 = W / 2 - total / 2;
            bool menu = Show(HudPart.MenuButtons);
            float menuH = menu ? 36f : 0f;
            float y0 = H - 16 - menuH - 18 - slot;

            if (Show(HudPart.Abilities))
            {
                Block(new Rect(x0 - 8, y0 - 8, total + 16, slot + 26));
                UiSkin.Fill(new Rect(x0 + slot * 0.5f, y0 + slot * 0.5f, total - slot, 1), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.3f));
                var invisible = new GUIStyle();
                for (int i = 0; i < 4; i++)
                {
                    var a = pl.Abilities[i];
                    if (a == null) continue;
                    var r = new Rect(x0 + i * (slot + gap), y0, slot, slot);
                    bool affordable = a.Cost == null || Game.Bank.CanAfford(a.Cost);
                    bool ready = a.CooldownLeft <= 0f && affordable;
                    bool hover = r.Contains(_mouseGui);
                    var edge = !affordable ? new Color(0.45f, 0.5f, 0.55f, 0.8f) : hover ? Color.white : UiSkin.Cyan;
                    UiSkin.Hex(r, new Color(0.02f, 0.08f, 0.12f, 0.9f), true);
                    if (ready) UiSkin.Hex(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.12f + (hover ? 0.12f : 0f)), true);
                    UiSkin.Hex(r, edge, false);
                    // перезарядка — тёмная заливка шестигранника снизу вверх
                    if (a.CooldownLeft > 0f)
                    {
                        float cf = a.CooldownFraction;
                        GUI.BeginGroup(new Rect(r.x, r.y + r.height * (1f - cf), r.width, r.height * cf));
                        UiSkin.Hex(new Rect(0, -r.height * (1f - cf), r.width, r.height), new Color(0, 0, 0, 0.65f), true);
                        GUI.EndGroup();
                    }
                    var code = new GUIStyle(UiSkin.Mono) { alignment = TextAnchor.MiddleCenter, fontSize = 17, fontStyle = FontStyle.Bold };
                    code.normal.textColor = affordable ? UiSkin.Text : UiSkin.Dim;
                    GUI.Label(new Rect(r.x, r.y + r.height * 0.3f, r.width, 24), a.CooldownLeft > 0f ? $"{a.CooldownLeft:0.0}" : AbilityCodes[i], code);
                    var key = new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                    key.normal.textColor = UiSkin.Cyan;
                    GUI.Label(new Rect(r.x, r.y + 7, r.width, 16), (i + 1).ToString(), key);
                    GUI.Label(new Rect(r.x - 10, r.yMax - 2, r.width + 20, 16), a.Name.ToUpperInvariant(), new GUIStyle(UiSkin.Tiny) { alignment = TextAnchor.UpperCenter, fontSize = 10, wordWrap = false });
                    string tip = $"<b>{a.Name}</b> [{i + 1}]: {a.Description}\nЦена: {(a.Cost != null ? a.Cost.ToString() : "—")} · перезарядка {a.Cooldown:0} с";
                    if (GUI.Button(r, new GUIContent("", tip), invisible)) pl.UseAbility(i);
                    if (tut != null && tut.Active && tut.HighlightAbility == i && !tut.Modal) UiSkin.Highlight(r);
                }
            }

            if (menu)
            {
                string[] labels = { "СТРОЙКА", "НАВЫКИ", "ЗАДАЧИ", "ГАРНИЗОН", "КАРТА", "СПРАВКА" };
                string[] keys = { "B", "K", "J", "G", "M", "F1" };
                UiWindow[] wins = { UiWindow.Build, UiWindow.Skills, UiWindow.Tasks, UiWindow.Garrison, UiWindow.GlobalMap, UiWindow.Help };
                int count = 0;
                for (int i = 0; i < wins.Length; i++) if (tut == null || tut.WindowRevealed(wins[i])) count++;
                float bw = 96f, bg = 6f;
                float mx = W / 2 - (count * bw + (count - 1) * bg) / 2f, my = H - 16 - 28;
                int k = 0;
                for (int i = 0; i < wins.Length; i++)
                {
                    if (tut != null && !tut.WindowRevealed(wins[i])) continue;
                    var br = new Rect(mx + k * (bw + bg), my, bw, 28);
                    k++;
                    Block(br);
                    var lockF = RequiredFeature(wins[i]);
                    bool locked = lockF.HasValue && !Game.Rank.IsUnlocked(lockF.Value);
                    var style = Window == wins[i] ? UiSkin.TabActive : locked ? UiSkin.ButtonDisabled : UiSkin.Tab;
                    string keyCol = locked ? UiSkin.Hex(UiSkin.Dim) : UiSkin.Hex(UiSkin.Cyan);
                    if (GUI.Button(br, $"<color=#{keyCol}><b>{keys[i]}</b></color> {labels[i]}", style)) Open(wins[i]);
                    if (tut != null && tut.Active && tut.HighlightWindow == wins[i] && Window != wins[i] && !tut.Modal) UiSkin.Highlight(br);
                }
            }

            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                float tw = 420f;
                float th = UiSkin.Small.CalcHeight(new GUIContent(GUI.tooltip), tw - 20) + 14;
                var tr = new Rect(W / 2 - tw / 2, y0 - th - 14, tw, th);
                GUI.Box(tr, GUIContent.none, UiSkin.PanelLight);
                GUI.Label(new Rect(tr.x + 10, tr.y + 6, tr.width - 20, tr.height - 10), GUI.tooltip, UiSkin.Small);
            }
        }

        // ------------------------------------------------------------ подсказка режима стройки

        void DrawBuildHint()
        {
            var b = Game.Build;
            if (b == null || !b.IsPlacing) return;
            var d = b.Selected;
            var r = new Rect(W / 2 - 340, H - 290, 680, 66);
            Block(r);
            UiSkin.Panel2(r);
            GUI.Label(new Rect(r.x + 14, r.y + 8, 652, 22), $"<b>{d.Name.ToUpperInvariant()}</b>  ·  {b.CostOf(d)}  ·  R — поворот  ·  ПКМ/Esc — отмена{(d.LinePlacement ? "  ·  тяните мышью для линии" : "  ·  Shift — несколько")}", UiSkin.Small);
            string why = b.LastInvalidReason;
            string ok = b.OutOfPower ? UiSkin.Col("▲ Место подходит, но вне энергосети — поставьте энергоузел рядом", UiSkin.Accent) : UiSkin.Col("● Место подходит", UiSkin.Good);
            GUI.Label(new Rect(r.x + 14, r.y + 34, 652, 22), why == null ? ok : UiSkin.Col("✕ " + why, UiSkin.Bad), UiSkin.Small);
        }

        // ------------------------------------------------------------ голографический радар

        void DrawRadar()
        {
            var pl = Game.Player;
            if (pl == null) return;
            float size = 190f;
            var r = new Rect(W - 16 - size - 6, H - BottomPanelH - 16 - 14 - size, size, size);
            Block(r);
            UiSkin.Tex(r, UiSkin.RadarBack, Color.white);
            // вращающийся луч
            float ang = Mathf.Repeat(Time.unscaledTime * 110f, 360f);
            var m = UiSkin.Rotate(ang, r.center);
            UiSkin.Tex(r, UiSkin.Sweep, Color.white);
            GUI.matrix = m;

            var c = pl.transform.position;
            float range = 70f;
            float rad = size * 0.5f - 6f;
            float yaw = Game.CameraRig != null ? Game.CameraRig.Yaw : 0f;
            var rot = Quaternion.Euler(0, -yaw, 0);
            Vector2 ToRadar(Vector3 world)
            {
                var d = rot * (world - c);
                return new Vector2(d.x, -d.z) / range * rad;
            }
            void Dot(Vector3 world, Color col, float s)
            {
                var v = ToRadar(world);
                if (v.magnitude > rad) return;
                UiSkin.Tex(new Rect(r.center.x + v.x - s / 2, r.center.y + v.y - s / 2, s, s), UiSkin.Dot, col);
            }
            foreach (var s in Registry.Structures) if (s != null && !s.IsDestroyed) Dot(s.transform.position, s.IsTargetCore ? UiSkin.Accent : new Color(0.55f, 0.8f, 0.9f, 0.55f), s.IsTargetCore ? 10f : 5f);
            foreach (var d in Registry.Deposits) if (d != null && !d.Hidden) Dot(d.transform.position, ResourceBank.ColorOf(d.Type), 5f);
            float blink = 0.6f + 0.4f * UiSkin.Pulse(6f);
            foreach (var e in Registry.Enemies) if (e != null && !e.IsDead) Dot(e.transform.position, e.IsHostile ? new Color(UiSkin.Bad.r, UiSkin.Bad.g, UiSkin.Bad.b, blink) : new Color(0.9f, 0.8f, 0.4f, 0.7f), e.Archetype.Kind == EnemyKind.Tank ? 11f : 6f);
            foreach (var n in Registry.Npcs) if (n != null && !n.IsAway) Dot(n.transform.position, UiSkin.Good, 5f);

            // маркер обучения — ромб (у края, если далеко)
            var mp = Game.Tutorial?.MarkerPosition;
            if (mp.HasValue)
            {
                var v = ToRadar(mp.Value);
                if (v.magnitude > rad - 4f) v = v.normalized * (rad - 4f);
                float s = 12f + 4f * UiSkin.Pulse(5f);
                UiSkin.Tex(new Rect(r.center.x + v.x - s / 2, r.center.y + v.y - s / 2, s, s), UiSkin.Diamond, UiSkin.Cyan);
            }
            // игрок — стрелка взгляда камеры
            UiSkin.Tex(new Rect(r.center.x - 5, r.center.y - 5, 10, 10), UiSkin.Diamond, Color.white);
            UiSkin.Brackets(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.5f), 12f, 2f);
            GUI.Label(new Rect(r.x, r.y - 2, r.width, 16), "РАДАР · 70 М", new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperCenter, fontSize = 10 });
            GUI.Label(new Rect(r.x, r.yMax - 16, r.width, 16), $"КОНТАКТЫ {Game.Enemies.CountHostile()}", new GUIStyle(UiSkin.MonoSmall) { alignment = TextAnchor.UpperCenter, fontSize = 10 });
        }

        // ------------------------------------------------------------ указатель цели обучения у края экрана

        void DrawMarkerPointer()
        {
            var tut = Game.Tutorial;
            if (tut == null || !tut.Active || tut.Modal) return;
            var mp = tut.MarkerPosition;
            var cam = Game.CameraRig != null ? Game.CameraRig.Cam : null;
            if (!mp.HasValue || cam == null) return;
            var sp = cam.WorldToScreenPoint(mp.Value + Vector3.up * 2f);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
            var g = new Vector2(sp.x / _scale, (Screen.height - sp.y) / _scale);
            float dist = Game.Player != null ? Vector3.Distance(Game.Player.transform.position, mp.Value) : 0f;
            var distStyle = new GUIStyle(UiSkin.Mono) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            distStyle.normal.textColor = UiSkin.Cyan;
            const float margin = 70f;
            bool onScreen = !behind && g.x > margin && g.x < W - margin && g.y > margin && g.y < H - margin;
            if (onScreen)
            {
                if (dist > 6f) GUI.Label(new Rect(g.x - 50, g.y - 34, 100, 20), $"{dist:0} М", distStyle);
                return;
            }
            var center = new Vector2(W / 2f, H / 2f);
            var dir = g - center;
            if (dir.sqrMagnitude < 1f) dir = Vector2.up;
            float kx = (W / 2f - margin) / Mathf.Max(0.001f, Mathf.Abs(dir.x));
            float ky = (H / 2f - margin) / Mathf.Max(0.001f, Mathf.Abs(dir.y));
            var p = center + dir * Mathf.Min(kx, ky);
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float s = 34f + 6f * UiSkin.Pulse(5f);
            var m = UiSkin.Rotate(ang, p);
            UiSkin.Tex(new Rect(p.x - s / 2, p.y - s / 2, s, s), UiSkin.Arrow, UiSkin.Cyan);
            GUI.matrix = m;
            var back = p - dir.normalized * 34f;
            GUI.Label(new Rect(back.x - 50, back.y - 10, 100, 20), $"{dist:0} М", distStyle);
        }
    }
}
