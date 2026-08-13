#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>Two restrained gear colors from one shared material and rig.</summary>
    [DisallowMultipleComponent]
    public sealed class HumanVisualVariant : MonoBehaviour
    {
        [SerializeField] private HumanCombatConfig? _config = null;
        [SerializeField] private Renderer[] _gear = System.Array.Empty<Renderer>();
        private MaterialPropertyBlock? _block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            Apply();
        }

        private void OnEnable() => Apply();

        private void Apply()
        {
            if (_config == null) return;
            _block ??= new MaterialPropertyBlock();
            Color color = (Mathf.Abs(GetInstanceID()) & 1) == 0 ? _config.variantA : _config.variantB;
            _block.SetColor(BaseColorId, color);
            for (int i = 0; i < _gear.Length; i++)
            {
                if (_gear[i] != null) _gear[i].SetPropertyBlock(_block);
            }
        }

#if UNITY_EDITOR
        public void Configure(HumanCombatConfig config, Renderer[] gear)
        {
            _config = config;
            _gear = gear;
        }
#endif
    }
}
