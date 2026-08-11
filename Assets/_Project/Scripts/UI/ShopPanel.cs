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
        [Tooltip("Optional. Shop keys are ignored while paused — SPACE means 'confirm' there and 'next wave' here.")]
        [SerializeField] private PausePanel? _pause = null;
        [SerializeField] private AudioSource? _audio = null;
        [SerializeField] private AudioClip? _buyClip = null;
        [SerializeField] private AudioClip? _refusedClip = null;

        // One builder, reused. Rebuilding the list allocates a string per redraw,
        // and a redraw happens on every purchase — rare, but there is no reason
        // to hand the collector work in a horde game.
        private readonly StringBuilder _builder = new(512);

        /// <summary>
        /// Index-aligned with the offer list. Covers ShopConfig.offersPerBreak's
        /// full Range(1, 8) so the keys and the printed numbers can never disagree.
        /// </summary>
        private static readonly Key[] BuyKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
            Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8,
            // Nine, because the always-offered repair and resupply rows are
            // appended after the drawn offers and have to be reachable too.
            Key.Digit9,
        };

        /// <summary>How many rows the keyboard can actually reach. ShopConfig warns past this.</summary>
        public static int BuyableRows => BuyKeys.Length;

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
            // The break takes the controls. Without this the player walked,
            // jumped and fired behind a full-screen shop, and R and SPACE were
            // bound to the shop AND to the Player action map at the same time.
            _pause?.SetPlayerControlsBlocked(open);
            if (open) Redraw();
        }

        private void Show(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        private void Update()
        {
            if (_runner == null || _runner.Phase != RunPhase.Shop) return;
            // OwnsInputThisFrame, not IsPaused: SPACE resumes the pause menu and
            // starts the next wave, and reading the cleared flag in the same frame
            // did both at once. See PausePanel.OwnsInputThisFrame.
            if (_pause != null && _pause.OwnsInputThisFrame) return;

            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Every digit the list can print, not the four it happened to start
            // with. ShopConfig.offersPerBreak allows up to 8, Redraw numbers all of
            // them, and pressing 5 on a drawn offer used to do nothing at all.
            for (int i = 0; i < BuyKeys.Length; i++)
            {
                if (!keyboard[BuyKeys[i]].wasPressedThisFrame) continue;
                // One purchase per frame. A successful Buy REMOVES the offer, so a
                // second digit in the same frame indexed a list that had already
                // shifted underneath it — pressing 1 and 2 together bought the item
                // printed as 3.
                Buy(i);
                break;
            }
            if (keyboard[Key.R].wasPressedThisFrame) Reroll();
            if (keyboard[Key.Space].wasPressedThisFrame) _runner.ContinueFromShop();
            // TAB is free across the whole project — the only keys polled anywhere
            // are the digits, R, SPACE, WASD, ENTER, ESC, BACKSPACE and BACKQUOTE.
            // Polled here and nowhere else, per the one-input-owner-per-screen rule.
            if (keyboard[Key.Tab].wasPressedThisFrame) _runner.SkipShopForBonus();
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
                _builder.Clear();
                _builder.Append("R)  reroll  $").Append(shop.RerollCost)
                        .Append("        SPACE)  next wave");
                float skip = _runner.SkipBonusMultiplier;
                if (skip > 1f)
                {
                    _builder.Append("        TAB)  skip the shop, next clear pays x")
                            .Append(skip.ToString("0.##"));
                }
                _footerLabel.text = _builder.ToString();
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
