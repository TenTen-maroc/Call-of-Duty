#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// One rule that fires when a bullet lands. Stacking them is the product: a
    /// railgun with Pierce and Chain is one weapon asset with two entries in a
    /// list, not a new weapon class.
    ///
    /// THREE RULES, all of which exist because the obvious implementation is a
    /// crash or a corruption:
    ///
    /// 1. Modules are STATELESS. The asset holds numbers only. One asset is shared
    ///    by every weapon carrying it, and configs are read-only at runtime —
    ///    Domain Reload is off, so a module caching a target on itself would
    ///    persist that into the next Play session.
    ///
    /// 2. Modules never apply damage. They enqueue follow-ups and the weapon
    ///    applies them. That keeps double-dip prevention, the already-hit set and
    ///    the depth counter in exactly one place instead of four.
    ///
    /// 3. A module runs at depth 0 only, unless it opts in with maxDepth. Without
    ///    that, Explosive -> Chain -> Explosive is an infinite loop, and "chains
    ///    that chain" stops being a deliberate number and becomes a hang.
    /// </summary>
    public abstract class EffectModule : ScriptableObject
    {
        [Header("Recursion")]
        [Tooltip("0 = fires on the primary hit only. 1 = also fires on hits this shot caused. Read the class comment before raising it.")]
        [Range(0, 3)] public int maxDepth = 0;

        /// <summary>Extra targets the weapon should resolve along one ray. Pierce is the only module that changes the cast itself.</summary>
        public virtual int ExtraRayBudget => 0;

        /// <summary>Damage multiplier applied per additional target pierced. 1 = no loss.</summary>
        public virtual float PierceDamageFalloff => 1f;

        /// <summary>True when this module is allowed to fire at the given resolution depth.</summary>
        public bool RunsAtDepth(int depth) => depth <= maxDepth;

        /// <summary>
        /// Called for every hit this module is allowed to see. Query the world
        /// through the context's buffers, enqueue what should happen next, and
        /// return — never mutate this asset, and never damage anything directly.
        /// </summary>
        public abstract void Resolve(in HitContext context, FollowUpBuffer followUps);
    }

    /// <summary>
    /// Everything a module is allowed to know about one impact. A readonly struct
    /// passed by `in`, so hundreds of these per second cost nothing.
    /// </summary>
    public readonly struct HitContext
    {
        /// <summary>The weapon that fired. Owns the buffers, the pool and the already-hit set.</summary>
        public readonly WeaponController Shooter;
        public readonly WeaponConfig Config;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        /// <summary>Travel direction of the shot that caused this hit.</summary>
        public readonly Vector3 Direction;
        /// <summary>What took the damage. Null when the bullet hit geometry.</summary>
        public readonly Health? Target;
        public readonly float DamageDealt;
        /// <summary>0 = the primary hit. Each follow-up resolves one deeper.</summary>
        public readonly int Depth;

        public HitContext(WeaponController shooter, WeaponConfig config, Vector3 point, Vector3 normal,
            Vector3 direction, Health? target, float damageDealt, int depth)
        {
            Shooter = shooter;
            Config = config;
            Point = point;
            Normal = normal;
            Direction = direction;
            Target = target;
            DamageDealt = damageDealt;
            Depth = depth;
        }
    }

    public enum FollowUpKind
    {
        /// <summary>Damage a specific target. Chain and Explosive produce these.</summary>
        Damage,
        /// <summary>Cast a ray and damage whatever it finds first. Ricochet produces these.</summary>
        Ray,
    }

    /// <summary>One queued consequence of a hit. Applied by the weapon, never by the module that queued it.</summary>
    public struct FollowUp
    {
        public FollowUpKind Kind;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Damage;
        public float Range;
        public Health? Target;
        /// <summary>Depth this follow-up resolves AT — always the queuing context's depth + 1.</summary>
        public int Depth;
    }

    /// <summary>
    /// A fixed-capacity queue of follow-ups, owned and reused by the weapon.
    /// Fixed capacity is the second half of the recursion guard: even if a depth
    /// rule is mis-authored, a shot can never queue unbounded work.
    /// </summary>
    public sealed class FollowUpBuffer
    {
        private readonly FollowUp[] _items;
        private int _head;
        private int _count;

        public FollowUpBuffer(int capacity) => _items = new FollowUp[Mathf.Max(1, capacity)];

        public int Count => _count;
        public bool IsFull => _count >= _items.Length;

        /// <summary>Silently drops when full. A dropped chain jump is a missing spark; an unbounded queue is a frozen frame.</summary>
        public void Enqueue(in FollowUp item)
        {
            if (IsFull) return;
            _items[(_head + _count) % _items.Length] = item;
            _count++;
        }

        public bool TryDequeue(out FollowUp item)
        {
            if (_count == 0)
            {
                item = default;
                return false;
            }
            item = _items[_head];
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }
}
