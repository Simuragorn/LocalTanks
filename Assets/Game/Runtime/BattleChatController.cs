using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LocalTanks
{
    public enum BattleChatChannel
    {
        Team,
        All
    }

    /// <summary>A local visual chat shell. Only the player can add lines; there is no bot or network source.</summary>
    public sealed class BattleChatController : MonoBehaviour
    {
        private readonly List<string> messages = new List<string>();
        private VisualElement compose;
        private ScrollView history;
        private TextField input;
        private Button teamButton;
        private Button allButton;
        private Button sendButton;

        public BattleChatChannel Channel { get; private set; } = BattleChatChannel.Team;
        public bool IsComposing => compose != null && !compose.ClassListContains("hidden");
        public IReadOnlyList<string> Messages => messages;

        public void Configure(VisualElement root)
        {
            Unbind();
            history = root?.Q<ScrollView>("chat-history");
            compose = root?.Q<VisualElement>("chat-compose");
            input = root?.Q<TextField>("chat-input");
            teamButton = root?.Q<Button>("chat-team");
            allButton = root?.Q<Button>("chat-all");
            sendButton = root?.Q<Button>("chat-send");
            if (teamButton != null) teamButton.clicked += SelectTeam;
            if (allButton != null) allButton.clicked += SelectAll;
            if (sendButton != null) sendButton.clicked += SendCurrent;
            SetChannel(BattleChatChannel.Team);
            SetComposing(false);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!IsComposing && ModalUiState.AnyOpen) return;
            if (!IsComposing && keyboard.enterKey.wasPressedThisFrame)
            {
                SetComposing(true);
                return;
            }
            if (!IsComposing) return;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SetComposing(false);
                return;
            }
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                SetChannel(Channel == BattleChatChannel.Team ? BattleChatChannel.All : BattleChatChannel.Team);
                return;
            }
            if (keyboard.enterKey.wasPressedThisFrame) SendCurrent();
        }

        public void SetChannel(BattleChatChannel channel)
        {
            Channel = channel;
            teamButton?.EnableInClassList("selected", channel == BattleChatChannel.Team);
            allButton?.EnableInClassList("selected", channel == BattleChatChannel.All);
        }

        public void SetComposing(bool composing)
        {
            compose?.EnableInClassList("hidden", !composing);
            ModalUiState.Set(this, composing);
            if (composing) input?.Focus();
        }

        public bool SendText(string text)
        {
            string message = text?.Trim();
            if (string.IsNullOrEmpty(message)) return false;
            string prefix = Channel == BattleChatChannel.Team ? "[Команда]" : "[Всем]";
            string rendered = $"{prefix} Игрок: {message}";
            messages.Add(rendered);
            if (messages.Count > 8) messages.RemoveAt(0);
            RebuildHistory();
            return true;
        }

        private void SelectTeam() => SetChannel(BattleChatChannel.Team);
        private void SelectAll() => SetChannel(BattleChatChannel.All);

        private void SendCurrent()
        {
            if (SendText(input?.value) && input != null) input.value = string.Empty;
            SetComposing(false);
        }

        private void RebuildHistory()
        {
            VisualElement content = history?.contentContainer;
            if (content == null) return;
            content.Clear();
            foreach (string message in messages)
            {
                Label line = new Label(message);
                line.AddToClassList("chat-line");
                if (message.StartsWith("[Команда]")) line.AddToClassList("team");
                content.Add(line);
            }
            history.scrollOffset = new Vector2(0f, float.MaxValue);
        }

        private void OnDisable()
        {
            ModalUiState.Set(this, false);
            Unbind();
        }

        private void Unbind()
        {
            if (teamButton != null) teamButton.clicked -= SelectTeam;
            if (allButton != null) allButton.clicked -= SelectAll;
            if (sendButton != null) sendButton.clicked -= SendCurrent;
        }
    }
}
