#nullable enable
using System;
using CoD.Core;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// A thing in the arena the player can use: a terminal, a charge site, an
    /// extract pad, a data pad, a door.
    ///
    /// One component for all of them, because the difference between them is
    /// DATA — a kind, a prompt, how long the hold is, and whether it can be used
    /// more than once. A charge site and a terminal differ by two serialized
    /// fields, not by a class.
    ///
    /// It registers itself with the scene's InteractableRegistry when it turns
    /// on and removes itself when it turns off, so a mission that enables its
    /// extract pad half way through costs nothing until then and cannot be used
    /// early.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractPoint : MonoBehaviour, IInteractable
    {
        [SerializeField] private InteractableRegistry? _registry = null;

        [Header("What it is")]
        [SerializeField] private InteractKind _kind = InteractKind.Generic;
        [Tooltip("Shown verbatim. Built once here rather than composed per frame, because the HUD reads it every frame the player is in range.")]
        [SerializeField] private string _prompt = "USE";
        [Tooltip("0 = instant. A charge should cost real seconds; a data pad should not.")]
        [Min(0f)] [SerializeField] private float _holdSeconds = 1.5f;

        [Header("Rules")]
        [Tooltip("Off for a charge or an intel pickup — one use and it is spent. On for a door.")]
        [SerializeField] private bool _repeatable = false;
        [Tooltip("Starts refusing, and something else has to arm it. A locked door, an extract that is not called yet.")]
        [SerializeField] private bool _startsLocked = false;

        [Header("Feedback")]
        [SerializeField] private AudioSource? _audio = null;
        [SerializeField] private AudioClip? _useClip = null;
        [Tooltip("Hidden once spent, so a used charge site stops advertising itself.")]
        [SerializeField] private GameObject? _visual = null;

        private bool _spent;
        private bool _locked;

        /// <summary>Raised after a successful use. The director listens; this component never learns a mission exists.</summary>
        public event Action<InteractPoint>? Used;

        public InteractKind Kind => _kind;
        public string Prompt => _prompt;
        public float HoldSeconds => _holdSeconds;
        public Vector3 Position => transform.position;
        public bool CanInteract => !_spent && !_locked;

        /// <summary>Proven by tests, and read by the director when restoring a checkpoint.</summary>
        public bool IsSpent => _spent;

        private void Awake() => _locked = _startsLocked;

        private void OnEnable() => _registry?.Register(this);

        private void OnDisable() => _registry?.Unregister(this);

        /// <summary>Arms or disarms it. An extract pad is locked until the objective before it completes.</summary>
        public void SetLocked(bool locked) => _locked = locked;

        /// <summary>
        /// Back to unused. A checkpoint rewind has to undo this, or a mission
        /// retried after a death would start with its charges already planted
        /// and its objective already half complete.
        /// </summary>
        public void ResetPoint()
        {
            _spent = false;
            _locked = _startsLocked;
            if (_visual != null) _visual.SetActive(true);
        }

        public void Interact()
        {
            // Belt and braces. PlayerInteractor already refuses when CanInteract
            // is false, but this is public and a mission script could call it.
            if (!CanInteract) return;

            if (!_repeatable)
            {
                _spent = true;
                if (_visual != null) _visual.SetActive(false);
            }

            if (_audio != null && _useClip != null) _audio.PlayOneShot(_useClip);
            Used?.Invoke(this);
        }

#if UNITY_EDITOR
        public void Configure(InteractableRegistry registry, InteractKind kind, string prompt,
            float holdSeconds, GameObject visual, AudioSource? audio = null, AudioClip? useClip = null)
        {
            _registry = registry;
            _kind = kind;
            _prompt = prompt;
            _holdSeconds = Mathf.Max(0f, holdSeconds);
            _repeatable = false;
            _startsLocked = false;
            _visual = visual;
            _audio = audio;
            _useClip = useClip;
        }

        private void OnValidate()
        {
            // An empty prompt is an interactable the player cannot see, which
            // looks exactly like an interactable that is broken.
            if (string.IsNullOrWhiteSpace(_prompt)) _prompt = "USE";
        }
#endif
    }
}
