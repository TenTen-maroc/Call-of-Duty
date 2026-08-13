#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>Resets a severed part's rigidbody on every pool reuse.</summary>
    [DisallowMultipleComponent]
    public sealed class PooledGorePart : MonoBehaviour
    {
        [SerializeField] private Rigidbody? _body = null;

        private void Awake()
        {
            if (_body == null) TryGetComponent(out _body);
        }

        private void OnEnable()
        {
            if (_body == null) return;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }

#if UNITY_EDITOR
        public void Configure(Rigidbody body) => _body = body;
#endif
    }
}
