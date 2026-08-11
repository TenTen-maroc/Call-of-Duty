#nullable enable
using System.Text;
using CoD.Core;
using CoD.Waves;
using CoD.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// The between-wave shop. Keyboard-driven on purpose: number keys buy, R
    /// rerolls, Space continues.
    ///
    /// Why not clickable buttons — uGUI buttons need an EventSystem, an input
    /// module, and a cursor that has to be unlocked and re-locked around every
    /// break, which is three new failure modes for a four-item list in a game
    /// where both hands are already on the keyboard. If the shop grows past a
    /// screenful, revisit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopPanel : MonoBehaviour
    {
        [SerializeField] private WaveRunner? _runner = null;
        [SerializeField] private RunContext? _run = null;
        [Tooltip("Root object toggled with the shop phase.")]
        [SerializeField] private GameObject? _root = null;
        [SerializeField] private Text? _titleLabel = null;
        [SerializeField] private Text? _offersLabel = null;
        [SerializeField] private Text? _footerLabel = null;
        [Tooltip("Shows the weapon's installed effect modules, in order. Stacking is the product; an unreadable stack is an unsold stack.")]
        [SerializeField] private Text? _loadoutLabel = null;
        [SerializeField] private WeaponController? _weapon = null;
        [SerializeField] private AudioSource? _audio = null;
        [SerializeField] private AudioClip? _buyClip = null;
        [SerializeField] private AudioClip? _refusedClip = null;

        // One builder, reused. Rebuilding the list allocates a string per redraw,
        // and a redraw happens on every purchase — rare, but there is no reason
        // to hand the collector work in a horde game.
        private readonly StringBuilder _builder = new(512);

        private void OnEnable()
        {
            if (_runner != null) _runner.PhaseChanged += OnPhaseChanged;
            Show(false);
        }

        private void OnDisable()
        {
            if (_runner != null) _runner.PhaseChanged -= OnPhaseChanged;
        }

        private void OnPhaseChanged(RunPhase phase)
        {
            bool open = phase == RunPhase.Shop;
            Show(open);
            if (open) Redraw();
        }

        private void Show(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        private void Update()
        {
            if (_runner == null || _runner.Phase != RunPhase.Shop) return;

            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[Key.Digit1].wasPressedThisFrame) Buy(0);
            if (keyboard[Key.Digit2].wasPressedThisFrame) Buy(1);
            if (keyboard[Key.Digit3].wasPressedThisFrame) Buy(2);
            if (keyboard[Key.Digit4].wasPressedThisFrame) Buy(3);
            if (keyboard[Key.R].wasPressedThisFrame) Reroll();
            if (keyboard[Key.Space].wasPressedThisFrame) _runner.ContinueFromShop();
        }

        private void Buy(int index)
        {
            ShopService? shop = _runner != null ? _runner.Shop : null;
            if (shop == null || _runner == null) return;

            bool bought = shop.TryBuy(index, _runner.WaveNumber + 1);
            Play(bought ? _buyClip : _refusedClip);
            if (bought) Redraw();
        }

        private void Reroll()
        {
            ShopService? shop = _runner != null ? _runner.Shop : null;
            if (shop == null || _runner == null) return;

            bool rerolled = shop.TryReroll(_runner.WaveNumber + 1);
            Play(rerolled ? _buyClip : _refusedClip);
            if (rerolled) Redraw();
        }

        private void Play(AudioClip? clip)
        {
            if (_audio != null && clip != null) _audio.PlayOneShot(clip);
        }

        private void Redraw()
        {
            ShopService? shop = _runner != null ? _runner.Shop : null;
            if (shop == null || _runner == null || _run == null) return;

            if (_titleLabel != null)
            {
                _titleLabel.text = "SHOP  —  BEFORE WAVE " + (_runner.WaveNumber + 1) + "   $ " + _run.State.Money;
            }

            if (_offersLabel != null)
            {
                _builder.Clear();
                for (int i = 0; i < shop.Offers.Count; i++)
                {
                    ShopItemConfig item = shop.Offers[i];
                    int price = shop.Prices[i];
                    bool affordable = _run.State.CanAfford(price);

                    _builder.Append(i + 1).Append(")  ");
                    // Unaffordable lines are marked in text, not colour: one Text
                    // component cannot colour part of a line without rich text,
                    // and this reads fine at a glance either way.
                    _builder.Append(affordable ? "" : "[$] ");
                    _builder.Append(item.displayName).Append("   $").Append(price);
                    if (!string.IsNullOrEmpty(item.description))
                    {
                        _builder.Append("\n       ").Append(item.description);
                    }
                    _builder.Append('\n');
                }
                if (shop.Offers.Count == 0) _builder.Append("Sold out. Nothing left to offer this break.");
                _offersLabel.text = _builder.ToString();
            }

            if (_footerLabel != null)
            {
                _footerLabel.text = "R)  reroll  $" + shop.RerollCost + "        SPACE)  next wave";
            }

            RedrawLoadout();
        }

        /// <summary>
        /// The installed module list, in execution order. Order is a real rule —
        /// a module can only react to what an earlier one produced — so showing it
        /// as an ordered line is showing the mechanic, not decoration.
        /// </summary>
        private void RedrawLoadout()
        {
            if (_loadoutLabel == null) return;
            if (_weapon == null || _weapon.EffectModuleCount == 0)
            {
                _loadoutLabel.text = "RIFLE:  no effect modules installed";
                return;
            }

            _builder.Clear();
            _builder.Append("RIFLE:  ");
            for (int i = 0; i < _weapon.EffectModuleCount; i++)
            {
                EffectModule? module = _weapon.EffectModuleAt(i);
                if (module == null) continue;
                if (i > 0) _builder.Append("  >  ");
                _builder.Append(module.name.Replace("Effect_", string.Empty));
            }
            _loadoutLabel.text = _builder.ToString();
        }
    }
}
