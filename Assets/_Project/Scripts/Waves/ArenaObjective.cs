#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// A repair beacon that moves to a different lane every wave.
    ///
    /// The arena has three lanes and a solid centre, and nothing in the game gave
    /// the player a reason to be in one lane rather than another — so the correct
    /// play was to find a corner with good sightlines and never leave it. This is
    /// the smallest thing that changes that: a heal you have to walk to, in a
    /// place that is not where it was last wave, with a budget small enough that
    /// going is a decision rather than a routine.
    ///
    /// Everything it touches is serialized in. Nothing is looked up per frame,
    /// there is no static state, and it allocates nothing while running.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaObjective : MonoBehaviour
    {
        [SerializeField] private ObjectiveConfig? _config = null;
        [Tooltip("Relocates on WaveStarted, and only heals during a wave.")]
        [SerializeField] private WaveRunner? _runner = null;
        [SerializeField] private Transform? _player = null;
        [SerializeField] private Health? _playerHealth = null;
        [Tooltip("One per lane. The beacon never picks the same one twice in a row.")]
        [SerializeField] private Transform[] _anchors = System.Array.Empty<Transform>();
        [Tooltip("What actually moves, so this component can live on a fixed parent.")]
        [SerializeField] private Transform? _visual = null;

        private float _budgetRemaining;
        private int _lastAnchor = -1;

        /// <summary>Where the beacon currently is. Used by the tests to prove it moved.</summary>
        public Vector3 Position => _visual != null ? _visual.position : transform.position;

        /// <summary>Health it can still give this wave.</summary>
        public float BudgetRemaining => _budgetRemaining;

        private void OnEnable()
        {
            if (_runner != null) _runner.WaveStarted += OnWaveStarted;
            // Placed once on entry as well: the first wave starts before anything
            // has raised WaveStarted, and a beacon sitting at the origin would be
            // inside the centre bunker.
            Relocate();
        }

        private void OnDisable()
        {
            if (_runner != null) _runner.WaveStarted -= OnWaveStarted;
        }

        private void OnWaveStarted(int wave) => Relocate();

        private void Relocate()
        {
            if (_config != null) _budgetRemaining = _config.healBudgetPerWave;
            if (_anchors.Length == 0 || _visual == null) return;

            int index = NextAnchorIndex();
            _lastAnchor = index;

            Transform? anchor = _anchors[index];
            if (anchor != null) _visual.position = anchor.position;
        }

        /// <summary>
        /// A random lane that is not the one it was just in. Picking from a range
        /// one shorter and stepping over the previous index gives a uniform choice
        /// without ever looping to reroll.
        /// </summary>
        private int NextAnchorIndex()
        {
            if (_anchors.Length <= 1) return 0;
            if (_lastAnchor < 0) return Random.Range(0, _anchors.Length);

            int index = Random.Range(0, _anchors.Length - 1);
            if (index >= _lastAnchor) index++;
            return index;
        }

        private void Update()
        {
            if (_config == null || _playerHealth == null || _player == null || _visual == null) return;
            if (_budgetRemaining <= 0f) return;
            // Only during the fight. Healing through the shop break would make the
            // budget free, and the walk to it costs nothing when nothing is coming.
            if (_runner != null && _runner.Phase != RunPhase.Wave) return;

            Vector3 delta = _player.position - _visual.position;
            // Measured on the floor plane: the player's origin sits at their feet
            // and the beacon is a pad, so a spherical test would just be a
            // slightly smaller circle for no reason.
            delta.y = 0f;
            if (delta.sqrMagnitude > _config.radius * _config.radius) return;

            float wanted = Mathf.Min(_budgetRemaining, _config.healPerSecond * Time.deltaTime);
            // Only what was ACTUALLY restored comes off the budget. Standing on it
            // at full health must not quietly burn the wave's allowance.
            _budgetRemaining -= _playerHealth.Heal(wanted);
        }
    }
}
