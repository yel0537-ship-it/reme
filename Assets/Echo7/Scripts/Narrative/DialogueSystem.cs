using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Echo7
{
    public class DialogueChoice
    {
        public string Text;
        public Func<string> Unavailable;      // причина недоступности или null
        public Action Effect;
        public Func<DialogueNode> Next;       // null — закрыть диалог
        public string Hint;
    }

    public class DialogueNode
    {
        public string Speaker;
        public string Portrait;               // инициалы/значок
        public Color PortraitColor = new Color(0.3f, 0.35f, 0.4f);
        public string Text;
        public readonly List<DialogueChoice> Choices = new List<DialogueChoice>();

        public DialogueNode Add(string text, Action effect = null, Func<DialogueNode> next = null, Func<string> unavailable = null, string hint = null)
        {
            Choices.Add(new DialogueChoice { Text = text, Effect = effect, Next = next, Unavailable = unavailable, Hint = hint });
            return this;
        }
    }

    /// <summary>
    /// Диалоги с вариантами действий (вылечить, подбодрить, дать, взять, отказать…).
    /// На время диалога игра ставится на паузу.
    /// </summary>
    public class DialogueSystem
    {
        public DialogueNode Current { get; private set; }
        public bool IsOpen => Current != null;
        float _prevScale = 1f;

        public void Start(DialogueNode node)
        {
            if (node == null) return;
            if (!IsOpen) { _prevScale = Time.timeScale > 0f ? Time.timeScale : 1f; }
            Current = node;
            Time.timeScale = 0f;
            Game.Sound?.Play2D(Sfx.UiClick, 0.6f);
        }

        public void Choose(int index)
        {
            if (Current == null || index < 0 || index >= Current.Choices.Count) return;
            var c = Current.Choices[index];
            string why = c.Unavailable?.Invoke();
            if (why != null) { Game.Sound?.Play2D(Sfx.UiError, 0.5f); return; }
            Game.Sound?.Play2D(Sfx.UiClick, 0.6f);
            c.Effect?.Invoke();
            var next = c.Next?.Invoke();
            if (next != null) Current = next;
            else Close();
        }

        public void Close()
        {
            if (Current == null) return;
            Current = null;
            Time.timeScale = Game.Ui != null && Game.Ui.Paused ? 0f : _prevScale;
        }
    }

    /// <summary>Контент диалогов: разговоры с гарнизоном и связь с Центром.</summary>
    public static class DialogueLibrary
    {
        static DialogueNode Say(string speaker, string portrait, Color pc, string text) => new DialogueNode { Speaker = speaker, Portrait = portrait, PortraitColor = pc, Text = text };

        static string Initials(string name) => string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);

        public static DialogueNode NpcTalk(GarrisonNpc n)
        {
            var pc = n.Role == NpcRole.Medic ? new Color(0.7f, 0.25f, 0.25f) : n.Role == NpcRole.Engineer ? new Color(0.7f, 0.55f, 0.15f) : new Color(0.3f, 0.4f, 0.5f);
            string mood;
            if (n.Sickness > 0.2f) mood = "Командир... знобит. Эти твари с крыльями... Голова раскалывается.";
            else if (n.Morale >= 75) mood = "Всё под контролем, командир! Пусть только сунутся.";
            else if (n.Morale >= 50) mood = "Держимся, командир. Работы много, но справляемся.";
            else if (n.Morale >= 25) mood = "Честно? Устали все. Спим по очереди, едим что попало...";
            else mood = "Зачем мы здесь, командир? Центру на нас плевать. Я не подписывался умирать за камни.";
            var node = Say(n.DisplayName, Initials(n.Name), pc, $"{mood}\n\nЗдоровье: {Mathf.RoundToInt(n.Health.Current)}/{Mathf.RoundToInt(n.Health.Max)} · Мораль: {Mathf.RoundToInt(n.Morale)} ({Game.Garrison.MoraleWord(n.Morale)}) · {n.StateText}");

            node.Add("Подлечить (аптечка)", () =>
            {
                Game.Bank.Spend(ResourceType.Medkits, 1);
                n.Health.Heal(60f * Game.Skills.HealMult);
                n.Sickness = 0f;
                n.AddMorale(5f);
                Game.Fx.Emit(FxKind.Heal, n.transform.position + Vector3.up, 12, Vector3.up);
            }, () => Say(n.DisplayName, Initials(n.Name), pc, "Спасибо, командир. Так гораздо лучше."),
            () => !Game.Bank.Has(ResourceType.Medkits, 1) ? "Нет аптечек" : (n.Health.Fraction > 0.9f && n.Sickness <= 0f ? "Боец здоров" : null));

            node.Add("Подбодрить", null, () =>
            {
                n.LastTalkDay = Game.Clock.Day;
                bool works = n.Morale > 15f || Random.value < 0.5f;
                float gain = 6f + (int)Game.Rank.Current * 2f + Random.Range(0f, 6f);
                if (works) { n.AddMorale(gain); return Say(n.DisplayName, Initials(n.Name), pc, "…Да. Вы правы. Мы справимся. Спасибо, что не забываете про нас."); }
                n.AddMorale(-2f);
                return Say(n.DisplayName, Initials(n.Name), pc, "Красивые слова, командир. Слова не греют и не кормят.");
            }, () => n.LastTalkDay >= Game.Clock.Day ? "Сегодня уже говорили" : null, "Мораль +; раз в сутки");

            node.Add("Выдать доппаёк (2 пайка)", () => { Game.Bank.Spend(ResourceType.Rations, 2); n.AddMorale(8f); },
                () => Say(n.DisplayName, Initials(n.Name), pc, "О, нормальная еда! Вот это я понимаю — забота о личном составе."),
                () => Game.Bank.Has(ResourceType.Rations, 2) ? null : "Нет пайков");

            node.Add("Изъять боекомплект (+15 патронов)", () => { Game.Bank.Add(ResourceType.Ammo, 15); n.AddMorale(-10f); },
                () => Say(n.DisplayName, Initials(n.Name), pc, "Забирайте... Только когда полезут твари, я буду отбиваться прикладом."),
                null, "Мораль −10");

            if (n.Post == null)
                node.Add("Назначить расчётом ближайшей турели", () => Game.Garrison.AutoAssign(n),
                    () => Say(n.DisplayName, Initials(n.Name), pc, "Есть занять пост!"),
                    () => Game.Garrison.FreeTurrets().Count == 0 ? "Нет свободных турелей" : null);
            else
                node.Add($"Снять с поста ({n.Post.Def.Name})", () => Game.Defense.ClearCrew(n.Post));

            node.Add("Как обстановка на базе?", null, () => Say(n.DisplayName, Initials(n.Name), pc, Concerns()));
            node.Add("Вольно.");
            return node;
        }

        static string Concerns()
        {
            var g = Game.Garrison;
            var list = new List<string>();
            if (g.PresentCount > g.Beds) list.Add($"Тесно: коек {g.Beds}, а нас {g.PresentCount}. Нужен ещё жилой блок.");
            if (g.FoodFactor < 0.9f) list.Add("Пайков не хватает. Может, поохотиться на рогачей?");
            if (g.WaterFactor < 0.9f) list.Add("С водой беда — нужны конденсаторы, лучше у озера.");
            if (Game.Weather.IsHot) list.Add("Это пекло нас доконает. Механизмы перегреваются, турели клинит.");
            if (Game.Power.Satisfaction < 0.95f) list.Add("Энергии не хватает — турели работают вполсилы.");
            if (Game.Noise.Level > 20f) list.Add("Мы шумим на всю саванну. Фауна это слышит.");
            if (!Game.Bank.Has(ResourceType.Ammo, 60)) list.Add("Патроны на исходе.");
            if (list.Count == 0) list.Add("Жаловаться не на что, командир. Так держать.");
            return string.Join("\n", list);
        }

        static readonly Color CenterColor = new Color(0.2f, 0.45f, 0.65f);

        public static DialogueNode CenterComms()
        {
            var sup = Game.Supply;
            float eta = sup.HoursToDelivery;
            var node = Say("Центр управления колонией", "Ц", CenterColor,
                $"Аванпост «Эхо-7», на связи Центр. Репутация: {Game.Rank.Reputation}/100.\n" +
                $"Следующая плановая поставка через ~{Mathf.Max(0f, eta):0} ч: {sup.PlannedDelivery()}" +
                (sup.SkipNext > 0 ? "\n<color=#ff8866>Поставка будет пропущена — Центр вами недоволен.</color>" : "") +
                $"\nАктивных задач: {Game.Tasks.Active.Count}. Выполнено: {Game.Tasks.CompletedCount}, провалено: {Game.Tasks.FailedCount}.");

            node.Add("Открыть задачи Центра", () => Game.Ui.Open(UiWindow.Tasks));
            node.Add("Приоритет поставок (право голоса)", null, PriorityNode, () => Game.Rank.LockReason(Feature.CenterVoice));
            node.Add("Орбитальная доставка модулей", () => Game.Ui.Open(UiWindow.Orbital), null, () => Game.Rank.LockReason(Feature.OrbitalDelivery));
            node.Add("Запросить пополнение гарнизона (−15 репутации)", () =>
            {
                Game.Rank.AddReputation(-15, "запрос пополнения");
                Game.Supply.SendBonusPod(new Cost(), "Транспорт с пополнением");
                Game.Garrison.Recruit(2);
            }, () => Say("Центр управления колонией", "Ц", CenterColor, "Запрос одобрен. Двое бойцов уже в пути."),
            () => Game.Rank.LockReason(Feature.GarrisonCommand) ?? (Game.Rank.Reputation < 25 ? "Недостаточно репутации" : null));
            node.Add("Глобальная карта и перелёты", () => Game.Ui.Open(UiWindow.GlobalMap), null, () => Game.Rank.LockReason(Feature.GlobalMap));
            node.Add("Конец связи.");
            return node;
        }

        static DialogueNode PriorityNode()
        {
            var node = Say("Центр управления колонией", "Ц", CenterColor,
                $"Капитан, ваш голос учтён. Какой профиль поставок нужен аванпосту? Сейчас: {PriorityName(Game.Supply.Priority)}.");
            foreach (SupplyPriority p in System.Enum.GetValues(typeof(SupplyPriority)))
            {
                var pp = p;
                node.Add(PriorityName(pp), () => { Game.Supply.Priority = pp; Game.Log?.Center($"Профиль поставок: {PriorityName(pp)}."); }, CenterComms);
            }
            node.Add("Назад", null, CenterComms);
            return node;
        }

        public static string PriorityName(SupplyPriority p)
        {
            switch (p)
            {
                case SupplyPriority.Ammo: return "Боеприпасы";
                case SupplyPriority.Construction: return "Стройматериалы";
                case SupplyPriority.Provisions: return "Провизия и медикаменты";
            }
            return "Сбалансированный";
        }
    }
}
