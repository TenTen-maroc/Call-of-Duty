#nullable enable
using UnityEngine;

namespace CoD.UI
{
    /// <summary>
    /// Which row of a menu is selected. A plain C# class, not a MonoBehaviour and
    /// not a static — every menu owns its own instance, so opening the settings
    /// page cannot move the pause menu's selection under it.
    ///
    /// Wrapping is deliberate: with four rows and no mouse, walking off the
    /// bottom to reach the top is faster than reversing, and a cursor that stops
    /// dead at the last row reads as a stuck key.
    /// </summary>
    public sealed class MenuCursor
    {
        private int _count;

        public int Index { get; private set; }

        public MenuCursor(int count) => Count = count;

        /// <summary>Changing the row count re-clamps the selection rather than leaving it out of bounds.</summary>
        public int Count
        {
            get => _count;
            set
            {
                _count = Mathf.Max(0, value);
                Index = _count == 0 ? 0 : Mathf.Clamp(Index, 0, _count - 1);
            }
        }

        public void Move(int delta)
        {
            if (_count <= 0 || delta == 0) return;
            // Add _count before the modulo: C# % keeps the sign of the dividend,
            // so -1 % 4 is -1, not 3, and moving up from the first row would
            // index out of range.
            Index = ((Index + delta) % _count + _count) % _count;
        }

        public void SetIndex(int index)
        {
            if (_count <= 0) return;
            Index = Mathf.Clamp(index, 0, _count - 1);
        }

        public void Reset() => Index = 0;
    }
}
