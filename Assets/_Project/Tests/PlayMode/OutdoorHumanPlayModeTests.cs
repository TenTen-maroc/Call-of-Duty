#nullable enable
using System.Collections;
using CoD.Core;
using CoD.Enemies;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    public sealed class OutdoorHumanPlayModeTests
    {
        private const string SceneName = "11_AtlasOutpost";
        private readonly SaveFileGuard _save = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _save.CaptureAndReset();
            AsyncOperation? load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            Assert.IsNotNull(load, "the outdoor builder must register 11_AtlasOutpost");
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _save.Restore();
            yield return null;
        }

        [UnityTest]
        public IEnumerator OutdoorScene_HasColliderFreeArtAndReachableHiddenSpawns()
        {
            GameObject art = GameObject.Find("Art");
            Assert.IsNotNull(art);
            Assert.AreEqual(0, art.GetComponentsInChildren<Collider>(true).Length,
                "decorative nature/survival art must never own gameplay collision");

            DroneSpawner spawner = Find<DroneSpawner>();
            Transform spawnRoot = spawner.transform.Find("SpawnPoints");
            Assert.IsNotNull(spawnRoot);
            Assert.GreaterOrEqual(spawnRoot.childCount, 8);
            Transform player = Find<CharacterController>().transform;
            for (int i = 0; i < spawnRoot.childCount; i++)
            {
                var path = new NavMeshPath();
                bool calculated = NavMesh.CalculatePath(spawnRoot.GetChild(i).position, player.position,
                    NavMesh.AllAreas, path);
                Assert.IsTrue(calculated && path.status == NavMeshPathStatus.PathComplete,
                    spawnRoot.GetChild(i).name + " cannot reach the player on the committed NavMesh");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MeridianPool_SpawnsAtTwelveAndRestoresAfterExplosiveDeath()
        {
            DroneSpawner spawner = Find<DroneSpawner>();
            DroneRegistry registry = Find<DroneRegistry>();
            DroneConfig config = Require<DroneConfig>("Assets/_Project/Data/Drones/Meridian_Rifleman.asset");
            spawner.SetAliveCapOverride(12);
            Assert.AreEqual(12, spawner.SpawnBurst(config, 12));
            Assert.AreEqual(12, registry.AliveCount);
            Assert.IsNull(spawner.Spawn(config), "the human wave exceeded its hard alive cap");

            DroneController soldier = registry.Alive[0];
            HumanEnemyPresentation human = soldier.GetComponent<HumanEnemyPresentation>();
            Assert.IsNotNull(human);
            DamageInfo explosive = new(999f, soldier.transform.position + Vector3.up,
                Vector3.up, Vector3.right, false, HitRegion.Torso, DamageKind.Explosive);
            Assert.IsTrue(human.BeginDeath(in explosive));
            Assert.IsTrue(human.IsDeadPresentation);
            Assert.IsTrue(human.IsRagdoll);
            HitZone[] zones = soldier.GetComponentsInChildren<HitZone>(true);
            for (int i = 0; i < zones.Length; i++)
            {
                Collider zoneCollider = zones[i].GetComponent<Collider>();
                Assert.IsFalse(zoneCollider.enabled, "a dead hit zone can become bulletproof cover");
            }

            human.ResetForReuse();
            Assert.IsFalse(human.IsDeadPresentation);
            Assert.IsFalse(human.IsRagdoll);
            for (int i = 0; i < zones.Length; i++)
                Assert.IsTrue(zones[i].GetComponent<Collider>().enabled, "pool reuse did not restore a hit zone");

            registry.DespawnAll();
            yield return null;
        }

        private static T Find<T>() where T : Object
        {
            T? found = Object.FindFirstObjectByType<T>();
            Assert.IsNotNull(found, "missing scene component " + typeof(T).Name);
            return found!;
        }

        private static T Require<T>(string path) where T : Object
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, "missing generated asset " + path);
            return asset!;
        }
    }
}
