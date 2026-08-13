#nullable enable
using System;
using System.Collections;
using CoD.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CoD.Enemies
{
    /// <summary>
    /// Development-build-only staging for the deterministic Mission 2 visual
    /// route. It is inert in ordinary play and absent from release behavior.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Mission2CaptureDirector : MonoBehaviour
    {
        [SerializeField] private DroneSpawner? _spawner = null;
        [SerializeField] private DroneRegistry? _registry = null;
        [SerializeField] private DroneConfig? _rifleman = null;
        [SerializeField] private Transform? _player = null;
        [SerializeField] private Health? _playerHealth = null;
        [SerializeField] private SettingsHub? _settings = null;
        [SerializeField] private RangedBurst? _captureAttack = null;

        private readonly Vector3[] _stagingPositions =
        {
            new(-5f, 0f, -14f), new(0f, 0f, -13f), new(5f, 0f, -14f), new(-11f, 0f, -8f),
            new(11f, 0f, -8f), new(-15f, 0f, -2f), new(15f, 0f, -2f), new(0f, 0f, -4f),
        };
        private readonly DroneController?[] _staged = new DroneController?[8];
        private readonly NavMeshAgent?[] _stagedAgents = new NavMeshAgent?[8];
        private readonly Health?[] _stagedHealth = new Health?[8];

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!HasArgument("-codScreenshots") ||
                !string.Equals(ArgumentValue("-codMission"), "mission_02_hard_contact",
                    StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                return;
            }
            StartCoroutine(StageRoute());
#else
            enabled = false;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void LateUpdate()
        {
            if (_player == null) return;
            for (int i = 0; i < _staged.Length; i++)
            {
                DroneController? soldier = _staged[i];
                Health? health = _stagedHealth[i];
                if (soldier == null || health == null || !health.IsAlive ||
                    !soldier.gameObject.activeInHierarchy) continue;

                NavMeshAgent? agent = _stagedAgents[i];
                if (agent != null && agent.enabled && agent.isOnNavMesh)
                {
                    agent.Warp(_stagingPositions[i]);
                    agent.isStopped = true;
                }
                else
                {
                    soldier.transform.position = _stagingPositions[i];
                }

                Vector3 toPlayer = _player.position - soldier.transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.001f)
                    soldier.transform.rotation = Quaternion.LookRotation(toPlayer.normalized);
            }
        }

        private IEnumerator StageRoute()
        {
            if (_spawner == null || _registry == null || _rifleman == null || _player == null) yield break;
            if (_playerHealth != null) _playerHealth.Invulnerable = true;
            // The authored start is also the useful establishing composition:
            // far enough south to read both side lanes, the centre hut and the
            // ridgeline. The old -7 position filled the frame with the hut wall
            // and made every staged combat screenshot visually meaningless.
            _player.SetPositionAndRotation(new Vector3(0f, 1f, -25f), Quaternion.identity);
            yield return new WaitForSecondsRealtime(0.75f);

            // Tick the real director while standing in its authored approach
            // zone, then return to the established camera mark. This advances
            // through the same ObjectiveContext as ordinary play without relying
            // on a physics trigger (mission zones are distance checks).
            _player.position = new Vector3(0f, 1f, -2f);
            yield return null;
            yield return new WaitForSecondsRealtime(1f);
            _player.position = new Vector3(0f, 1f, -25f);
            yield return new WaitForSecondsRealtime(0.5f);

            _spawner.SetAliveCapOverride(12);
            _spawner.SpawnBurst(_rifleman, 8);
            yield return new WaitForSecondsRealtime(2f);
            StageSoldiers();
            if (_captureAttack != null) _captureAttack.projectilePrefab = null;
            yield return new WaitForSecondsRealtime(6.5f);

            SetFiringPose(true);
            yield return new WaitForSecondsRealtime(3f);
            ApplyImpactSet();
            yield return new WaitForSecondsRealtime(4f);

            ApplyExtremeDeaths();
            yield return new WaitForSecondsRealtime(4f);
            SetGore(GoreLevel.Reduced);
            ApplyRegionalHit(2, HitRegion.Torso, 8f, DamageKind.Direct);
            yield return new WaitForSecondsRealtime(4f);
            SetGore(GoreLevel.Off);
            ApplyRegionalHit(3, HitRegion.LeftArm, 8f, DamageKind.Direct);
        }

        private void StageSoldiers()
        {
            if (_registry == null) return;
            int count = Mathf.Min(_registry.Alive.Count, _stagingPositions.Length);
            for (int i = 0; i < count; i++)
            {
                DroneController soldier = _registry.Alive[i];
                _staged[i] = soldier;
                soldier.TryGetComponent(out _stagedHealth[i]);
                if (soldier.TryGetComponent(out NavMeshAgent agent) && agent.isOnNavMesh)
                {
                    agent.Warp(_stagingPositions[i]);
                    agent.isStopped = true;
                    _stagedAgents[i] = agent;
                }
                else
                    soldier.transform.position = _stagingPositions[i];
                Vector3 toPlayer = _player != null
                    ? _player.position - soldier.transform.position
                    : Vector3.back;
                toPlayer.y = 0f;
                soldier.transform.rotation = Quaternion.LookRotation(toPlayer.normalized);
            }
        }

        private void SetFiringPose(bool firing)
        {
            if (_registry == null) return;
            for (int i = 0; i < _registry.Alive.Count; i++) _registry.Alive[i].SetFiringPosture(firing);
        }

        private void ApplyImpactSet()
        {
            ApplyRegionalHit(0, HitRegion.Head, 7f, DamageKind.Direct);
            ApplyRegionalHit(1, HitRegion.Torso, 7f, DamageKind.Direct);
            ApplyRegionalHit(2, HitRegion.LeftArm, 7f, DamageKind.Direct);
            ApplyRegionalHit(3, HitRegion.LeftLeg, 7f, DamageKind.Direct);
            ApplyRegionalHit(4, HitRegion.Armor, 7f, DamageKind.Direct);
        }

        private void ApplyExtremeDeaths()
        {
            ApplyRegionalHit(0, HitRegion.Head, 999f, DamageKind.Direct);
            ApplyRegionalHit(1, HitRegion.Torso, 999f, DamageKind.Explosive);
        }

        private void ApplyRegionalHit(int index, HitRegion region, float damage, DamageKind kind)
        {
            if (_registry == null || index < 0 || index >= _registry.Alive.Count) return;
            DroneController soldier = _registry.Alive[index];
            if (!soldier.TryGetComponent(out Health health)) return;
            Vector3 point = soldier.transform.position + Vector3.up * 1.35f;
            DamageInfo info = new(damage, point, Vector3.back, Vector3.forward,
                region == HitRegion.Head, region, kind);
            health.ApplyDamage(in info);
        }

        private void SetGore(GoreLevel target)
        {
            if (_settings == null) return;
            for (int i = 0; i < 3 && _settings.Current.Gore != target; i++)
                _settings.Current.CycleGore(1);
            _settings.Apply();
        }

        private static bool HasArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return string.Empty;
        }
#endif

#if UNITY_EDITOR
        public void Configure(DroneSpawner spawner, DroneRegistry registry, DroneConfig rifleman,
            Transform player, Health playerHealth, SettingsHub settings, RangedBurst captureAttack)
        {
            _spawner = spawner;
            _registry = registry;
            _rifleman = rifleman;
            _player = player;
            _playerHealth = playerHealth;
            _settings = settings;
            _captureAttack = captureAttack;
        }
#endif
    }
}
