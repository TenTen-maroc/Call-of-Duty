#nullable enable
using UnityEngine.InputSystem;

namespace CoD.UI
{
    /// <summary>
    /// Menu navigation keys, read straight off the keyboard.
    ///
    /// WHY NOT THE .inputactions ASSET
    /// The Player action map is DISABLED while a menu is open — that is how
    /// pause stops the camera from turning under the panel. A menu bound to that
    /// map could never reopen itself. A second action map would work and is what
    /// a game with gamepad support needs; this is offline, keyboard-and-mouse,
    /// and one file of key polling is the smaller thing to maintain until a pad
    /// is on the list.
    ///
    /// WHY NOT uGUI BUTTONS AND AN EventSystem
    /// Same reason ShopPanel gives: buttons need an EventSystem, an input module,
    /// and a cursor unlocked and re-locked around every transition. Three new
    /// failure modes for a four-row list.
    ///
    /// A static class with only methods — allowed under the no-mutable-statics
    /// rule, which bans static FIELDS. There is no state here to survive a Play
    /// session.
    /// </summary>
    public static class MenuInput
    {
        /// <summary>-1 for up, +1 for down, 0 for neither. Arrows and WASD both work.</summary>
        public static int VerticalStep()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return 0;
            if (keyboard[Key.UpArrow].wasPressedThisFrame || keyboard[Key.W].wasPressedThisFrame) return -1;
            if (keyboard[Key.DownArrow].wasPressedThisFrame || keyboard[Key.S].wasPressedThisFrame) return 1;
            return 0;
        }

        /// <summary>-1 for left, +1 for right. What moves a slider.</summary>
        public static int HorizontalStep()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return 0;
            if (keyboard[Key.LeftArrow].wasPressedThisFrame || keyboard[Key.A].wasPressedThisFrame) return -1;
            if (keyboard[Key.RightArrow].wasPressedThisFrame || keyboard[Key.D].wasPressedThisFrame) return 1;
            return 0;
        }

        public static bool ConfirmPressed()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return keyboard[Key.Enter].wasPressedThisFrame
                   || keyboard[Key.NumpadEnter].wasPressedThisFrame
                   || keyboard[Key.Space].wasPressedThisFrame;
        }

        public static bool BackPressed()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return keyboard[Key.Escape].wasPressedThisFrame || keyboard[Key.Backspace].wasPressedThisFrame;
        }

        public static bool EscapePressed()
        {
            Keyboard? keyboard = Keyboard.current;
            return keyboard != null && keyboard[Key.Escape].wasPressedThisFrame;
        }
    }
}
