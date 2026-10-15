using UnityEngine.UIElements;

namespace LocalTanks
{
    /// <summary>
    /// Replaces Unity's editor-like runtime popup text surface with a small theme-owned presentation.
    /// The real DropdownField remains responsible for focus, clicks, keyboard input and its popup.
    /// </summary>
    public static class UiDropdownPresenter
    {
        private const string ShellName = "lt-dropdown-shell";
        private const string TextName = "lt-dropdown-readable-text";

        public static void Apply(DropdownField field)
        {
            if (field == null)
            {
                return;
            }

            field.AddToClassList("lt-dropdown");
            VisualElement existingShell = field.parent;
            if (existingShell != null && existingShell.name == ShellName)
            {
                Label existingText = existingShell.Q<Label>(TextName);
                if (existingText != null) existingText.text = field.value ?? string.Empty;
                return;
            }

            VisualElement parent = field.parent;
            if (parent == null)
            {
                return;
            }

            int index = parent.IndexOf(field);
            field.RemoveFromHierarchy();
            VisualElement shell = new VisualElement { name = ShellName };
            shell.AddToClassList("lt-dropdown-shell");
            Label readableText = new Label { name = TextName, pickingMode = PickingMode.Ignore };
            readableText.AddToClassList("lt-dropdown-readable-text");
            Label readableArrow = new Label("▼") { pickingMode = PickingMode.Ignore };
            readableArrow.AddToClassList("lt-dropdown-readable-arrow");
            shell.Add(field);
            shell.Add(readableText);
            shell.Add(readableArrow);
            parent.Insert(index, shell);

            field.AddToClassList("lt-dropdown-native-hitbox");
            readableText.text = field.value ?? string.Empty;
            field.RegisterValueChangedCallback(change => readableText.text = change.newValue ?? string.Empty);
            field.RegisterCallback<FocusInEvent>(_ => shell.AddToClassList("lt-dropdown-shell--focus"));
            field.RegisterCallback<FocusOutEvent>(_ => shell.RemoveFromClassList("lt-dropdown-shell--focus"));
        }
    }
}
