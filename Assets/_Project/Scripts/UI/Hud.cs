#nullable enable
using CoD.Core;
using CoD.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// Ammo and health readout. Deliberately tiny for the grey box — the point of
    /// the first milestone is the gun, not the interface.
    ///
    /// Text is only rebuilt when the underlying number changes. Assigning to
    /// Text.text every frame allocates a string every frame and dirties the
    /// canvas, which is one of the classic quiet framerate leaks in Unity UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Hud : MonoBehaviour
    {
        [SerializeField] private WeaponController? _weapon = null;
        [SerializeField] private Health? _playerHealth = null;
        [SerializeField] private Text? _ammoLabel = null;
        [SerializeField] private Text? _healthLabel = null;
        [SerializeField] private Graphic? _lowAmmoTint = null;
        [Range(0f, 1f)][SerializeField] private float _lowAmmoFraction = 0.25f;

        private int _lastAmmo = -1;
        private int _lastReserve = -1;
        private int _lastHealth = -1;
        private bool _lastReloading;

        private void Update()
        {
            UpdateAmmo();
            UpdateHealth();
        }

        private void UpdateAmmo()
        {
            if (_weapon == null || _ammoLabel == null) return;
            WeaponRuntime? runtime = _weapon.Runtime;
            if (runtime == null) return;

            // Reloading is part of the cache key: a reload that starts without an
            // ammo change (auto-reload on empty) must still flip the label to --.
            if (runtime.CurrentAmmo == _lastAmmo && runtime.ReserveAmmo == _lastReserve &&
                runtime.IsReloading == _lastReloading) return;
            _lastAmmo = runtime.CurrentAmmo;
            _lastReserve = runtime.ReserveAmmo;
            _lastReloading = runtime.IsReloading;

            _ammoLabel.text = runtime.IsReloading
                ? "-- / " + _lastReserve
                : _lastAmmo + " / " + _lastReserve;

            if (_lowAmmoTint != null)
            {
                // The magazine AS BUILT. Read off the config, an extended
                // magazine would tint low at the stock threshold — a 45-round
                // magazine going red at 6 rounds instead of 9.
                bool low = _lastAmmo <= Mathf.CeilToInt(runtime.MagazineSize * _lowAmmoFraction);
                _lowAmmoTint.enabled = low;
            }
        }

        private void UpdateHealth()
        {
            if (_playerHealth == null || _healthLabel == null) return;

            int current = Mathf.CeilToInt(_playerHealth.Current);
            if (current == _lastHealth) return;
            _lastHealth = current;
            _healthLabel.text = current.ToString();
        }
    }
}
