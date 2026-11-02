using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum MessageKind { Info, Warn, Alert, Center }

    /// <summary>Журнал событий: лента справа в HUD + всплывающие «тосты» для тревог.</summary>
    public class MessageLog
    {
        public struct Entry
        {
            public string Text;
            public MessageKind Kind;
            public float Time;
            public string Stamp;
        }

        public readonly List<Entry> Entries = new List<Entry>();
        public Entry? Toast { get; private set; }
        public float ToastUntil { get; private set; }
        const int Max = 80;

        void Add(string text, MessageKind kind)
        {
            if (string.IsNullOrEmpty(text)) return;
            // не спамим одинаковыми сообщениями
            if (Entries.Count > 0 && Entries[Entries.Count - 1].Text == text && UnityEngine.Time.unscaledTime - Entries[Entries.Count - 1].Time < 3f) return;
            var e = new Entry
            {
                Text = text,
                Kind = kind,
                Time = UnityEngine.Time.unscaledTime,
                Stamp = Game.Clock != null ? $"Д{Game.Clock.Day} {Game.Clock.TimeString}" : ""
            };
            Entries.Add(e);
            if (Entries.Count > Max) Entries.RemoveAt(0);
            if (kind == MessageKind.Alert || kind == MessageKind.Center)
            {
                Toast = e;
                ToastUntil = UnityEngine.Time.unscaledTime + 5f;
                if (kind == MessageKind.Center) Game.Sound?.Play2D(Sfx.Notify, 0.6f);
            }
            if (Application.isEditor && kind == MessageKind.Alert) Debug.Log("[Эхо-7] " + text);
        }

        public void Info(string t) => Add(t, MessageKind.Info);
        public void Warn(string t) => Add(t, MessageKind.Warn);
        public void Alert(string t) => Add(t, MessageKind.Alert);
        public void Center(string t) => Add(t, MessageKind.Center);

        public static Color ColorOf(MessageKind k)
        {
            switch (k)
            {
                case MessageKind.Warn: return new Color(1f, 0.8f, 0.4f);
                case MessageKind.Alert: return new Color(1f, 0.45f, 0.35f);
                case MessageKind.Center: return new Color(0.5f, 0.85f, 1f);
            }
            return new Color(0.85f, 0.87f, 0.9f);
        }
    }
}
