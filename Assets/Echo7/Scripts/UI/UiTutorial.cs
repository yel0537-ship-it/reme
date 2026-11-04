using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Интерфейс пролога: выбор режима начала, брифинги инструктора, баннер «задание выполнено».
    /// Цели, подсказки, указатель маркера и подсветки рисуются в UiHud.
    /// </summary>
    public partial class UiManager
    {
        int _bannerStep = -1;
        float _bannerUntil;
        string _bannerTitle = "";

        void DrawTutorialOverlays()
        {
            var tut = Game.Tutorial;
            if (tut == null) return;
            if (tut.ModeSelectOpen) { DrawModeSelect(tut); return; }
            if (tut.BriefingOpen && tut.Current != null) { DrawBriefing(tut); return; }
            DrawCompletionBanner(tut);
        }

        static void Portrait(Rect r, string letter, Color fill)
        {
            UiSkin.Hex(r, fill, true);
            UiSkin.Hex(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12), new Color(0, 0, 0, 0.35f), true);
            UiSkin.Hex(r, UiSkin.Cyan, false);
            var st = new GUIStyle(UiSkin.Big) { fontSize = Mathf.RoundToInt(r.height * 0.42f) };
            st.normal.textColor = UiSkin.Text;
            GUI.Label(r, letter, st);
            // голографическая развёртка поверх портрета
            float y = r.y + Mathf.Repeat(Time.unscaledTime * 40f, r.height);
            UiSkin.Fill(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 1), new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.35f));
        }

        // ------------------------------------------------------------ выбор режима

        void DrawModeSelect(TutorialDirector tut)
        {
            UiSkin.Fill(new Rect(0, 0, W, H), new Color(0f, 0.02f, 0.04f, 0.72f));
            Block(new Rect(0, 0, W, H));
            var r = WindowRect(860, 470);
            var c = UiSkin.Window(r, "Высадка на Эхо-7", "ПРОТОКОЛ НАЧАЛА СМЕНЫ");
            GUI.Label(new Rect(c.x, c.y, c.width, 48),
                "Шаттл ушёл на орбиту. Перед вами — пустой аванпост посреди саванны чужой планеты.\nКак начнём, командир?", UiSkin.Label);

            float cw = (c.width - 16) / 2f, ch = 300f, cy = c.y + 60;
            var left = new Rect(c.x, cy, cw, ch);
            var right = new Rect(c.x + cw + 16, cy, cw, ch);

            UiSkin.Panel2(left, true);
            UiSkin.Highlight(left);
            GUI.Label(new Rect(left.x + 16, left.y + 12, cw - 32, 24), "ПРОЛОГ: ОБУЧЕНИЕ", new GUIStyle(UiSkin.Title) { fontSize = 19 });
            GUI.Label(new Rect(left.x + 16, left.y + 38, cw - 32, 18), UiSkin.Col("РЕКОМЕНДУЕТСЯ ДЛЯ ПЕРВОЙ ИГРЫ", UiSkin.Good), UiSkin.MonoSmall);
            GUI.Label(new Rect(left.x + 16, left.y + 64, cw - 32, 170),
                "10 коротких учебных заданий с инструктором Центра: движение, связь, добыча, энергосеть, стены, турели, расчёт, бой, снабжение и первая ночь.\n\n" +
                "• время стоит, пока вы учитесь\n• волн, бурь и визитов нет\n• интерфейс открывается по шагам\n• обучение можно пропустить в любой момент", UiSkin.Small);
            if (GUI.Button(new Rect(left.x + 16, left.yMax - 48, cw - 32, 34), "Начать обучение  [1]", UiSkin.ButtonAccent)) tut.ChooseTutorial();

            UiSkin.Panel2(right, true);
            GUI.Label(new Rect(right.x + 16, right.y + 12, cw - 32, 24), "СРАЗУ В БОЙ", new GUIStyle(UiSkin.Title) { fontSize = 19 });
            GUI.Label(new Rect(right.x + 16, right.y + 38, cw - 32, 18), UiSkin.Col("ДЛЯ ТЕХ, КТО УЖЕ ЗНАЕТ ИГРУ", UiSkin.Accent), UiSkin.MonoSmall);
            GUI.Label(new Rect(right.x + 16, right.y + 64, cw - 32, 170),
                "Готовая стартовая база: турель, участок стены с воротами, энергоузлы и солнечная панель.\n\n" +
                "• время идёт сразу\n• все механики и окна открыты\n• Центр сразу выдаёт задачи\n• о новых механиках расскажут карточки-подсказки", UiSkin.Small);
            if (GUI.Button(new Rect(right.x + 16, right.yMax - 48, cw - 32, 34), "В бой  [2]", UiSkin.Button)) tut.ChooseClassic();

            GUI.Label(new Rect(c.x, r.yMax - 32, c.width, 20), "Режим по умолчанию задаётся в GameConfig → «Обучение».", new GUIStyle(UiSkin.Tiny) { alignment = TextAnchor.MiddleCenter });
        }

        // ------------------------------------------------------------ брифинг

        void DrawBriefing(TutorialDirector tut)
        {
            var s = tut.Current;
            UiSkin.Fill(new Rect(0, 0, W, H), new Color(0f, 0.02f, 0.04f, 0.5f));
            Block(new Rect(0, 0, W, H));
            float w = 900f;
            float textW = w - 200f;
            float textH = UiSkin.Label.CalcHeight(new GUIContent(s.Briefing), textW);
            float objH = UiSkin.Label.CalcHeight(new GUIContent(s.Objective), textW - 90);
            float h = Mathf.Min(H - 80, 50 + 20 + textH + 18 + objH + 24 + 64);
            var r = new Rect(W / 2 - w / 2, Mathf.Max(60f, H / 2 - h / 2 - 30f), w, h);
            var c = UiSkin.Window(r, $"Задание {tut.StepIndex + 1}/{tut.StepCount} · {s.Title}", "КАНАЛ ЦЕНТРА · ЗАЩИЩЁННЫЙ");

            var pr = new Rect(c.x + 4, c.y + 6, 120, 138);
            Portrait(pr, "Л", new Color(0.12f, 0.36f, 0.48f, 0.95f));
            GUI.Label(new Rect(c.x - 4, pr.yMax + 6, 136, 60), TutorialDirector.MentorName, new GUIStyle(UiSkin.Tiny) { alignment = TextAnchor.UpperCenter });
            // индикатор канала
            UiSkin.SegBar(new Rect(c.x + 14, pr.yMax + 50, 100, 5), 0.4f + 0.6f * UiSkin.Pulse(3f), 8, UiSkin.CyanDim, 2f);

            float x = c.x + 160;
            float y = c.y + 4;
            GUI.Label(new Rect(x, y, textW, textH), s.Briefing, UiSkin.Label);
            y += textH + 18;
            var objBox = new Rect(x, y, textW, objH + 20);
            UiSkin.Fill(objBox, new Color(UiSkin.Cyan.r, UiSkin.Cyan.g, UiSkin.Cyan.b, 0.07f));
            UiSkin.Fill(new Rect(objBox.x, objBox.y, 3, objBox.height), UiSkin.Cyan);
            GUI.Label(new Rect(objBox.x + 12, objBox.y + 10, 80, 20), "ЦЕЛЬ", UiSkin.Header);
            GUI.Label(new Rect(objBox.x + 84, objBox.y + 9, textW - 96, objH), s.Objective, UiSkin.Label);

            float by = r.yMax - 52;
            if (GUI.Button(new Rect(r.xMax - 250, by, 230, 36), "Понятно  [Пробел]", UiSkin.ButtonAccent)) { tut.CloseBriefing(); _modalClosedFrame = Time.frameCount; }
            bool confirm = Time.unscaledTime < _skipConfirmUntil;
            if (GUI.Button(new Rect(r.x + 20, by + 4, 220, 28), confirm ? UiSkin.Col("Точно пропустить?", UiSkin.Bad) : "Пропустить обучение", UiSkin.Tab))
            {
                if (confirm) { _skipConfirmUntil = 0f; tut.Skip(); _modalClosedFrame = Time.frameCount; }
                else _skipConfirmUntil = Time.unscaledTime + 3f;
            }
            if (!string.IsNullOrEmpty(s.Hint))
                GUI.Label(new Rect(r.x + 260, by + 8, r.width - 530, 22), UiSkin.Col("› " + s.Hint, UiSkin.Accent), new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false });
        }

        // ------------------------------------------------------------ баннер выполнения

        void DrawCompletionBanner(TutorialDirector tut)
        {
            var s = tut.Current;
            if (tut.Active && s != null && s.Progress() >= 1f && _bannerStep != tut.StepIndex)
            {
                _bannerStep = tut.StepIndex;
                _bannerUntil = Time.unscaledTime + 1.6f;
                _bannerTitle = s.Title.ToUpperInvariant();
            }
            if (Time.unscaledTime > _bannerUntil) return;
            float left = _bannerUntil - Time.unscaledTime;
            float a = Mathf.Clamp01(left * 3f) * Mathf.Clamp01((1.6f - left) * 6f);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            var r = new Rect(W / 2 - 300, H * 0.28f, 600, 64);
            UiSkin.Fill(new Rect(0, r.y + 6, W, r.height - 12), new Color(0f, 0.08f, 0.06f, 0.45f));
            UiSkin.Fill(new Rect(r.x, r.y + 6, r.width, 1), UiSkin.Good);
            UiSkin.Fill(new Rect(r.x, r.yMax - 7, r.width, 1), UiSkin.Good);
            var st = new GUIStyle(UiSkin.Big) { fontSize = 26 };
            st.normal.textColor = UiSkin.Good;
            GUI.Label(new Rect(r.x, r.y + 6, r.width, 32), "ЗАДАНИЕ ВЫПОЛНЕНО", st);
            GUI.Label(new Rect(r.x, r.y + 36, r.width, 20), _bannerTitle, new GUIStyle(UiSkin.Mono) { alignment = TextAnchor.MiddleCenter });
            GUI.color = old;
        }
    }
}
