#nullable enable
using System.Collections;
using System.Reflection;
using CoD.Core;
using CoD.UI;
using CoD.Waves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CoD.Tests
{
    public sealed class HumanizationPlayModeTests
    {
        private const string ArenaScene = "10_GreyBox";
        private const string MissionOneId = "mission_01_shakedown";
        private readonly SaveFileGuard _save = new();

        [UnitySetUp]
        public IEnumerator ResetSave()
        {
            _save.CaptureAndReset();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreSave()
        {
            _save.Restore();
            yield return null;
        }

        private static IEnumerator LoadArena()
        {
            AsyncOperation? load = SceneManager.LoadSceneAsync(ArenaScene, LoadSceneMode.Single);
            Assert.IsNotNull(load);
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator StoryCorner_IsVisiblePresentationWithNoGameplayCollision()
        {
            yield return LoadArena();
            GameObject room = GameObject.Find("Room");
            Assert.IsNotNull(room);
            Transform corner = room.transform.Find("StoryCorner_LastStand");
            Assert.IsNotNull(corner);
            Assert.GreaterOrEqual(corner.GetComponentsInChildren<Renderer>(true).Length, 6);
            Assert.AreEqual(0, corner.GetComponentsInChildren<Collider>(true).Length,
                "decorative evidence must not change collision, aim rays, or the baked navmesh");
        }

        [UnityTest]
        public IEnumerator MissionEntry_WithNullAudioStillPresentsReadableSubtitles()
        {
            SaveData save = SaveSystem.Load();
            save.campaignSelected = true;
            save.selectedMissionId = MissionOneId;
            SaveSystem.Save(save);
            yield return LoadArena();

            RadioDialogueScheduler? radio = Object.FindFirstObjectByType<RadioDialogueScheduler>();
            RadioSubtitleHud? hud = Object.FindFirstObjectByType<RadioSubtitleHud>();
            Assert.IsNotNull(radio);
            Assert.IsNotNull(hud);
            Assert.IsNotNull(radio!.Current);
            Assert.IsNull(radio.Current!.audioClip);

            Text label = Private<Text>(hud!, "_label");
            Image background = Private<Image>(hud!, "_background");
            Assert.IsTrue(label.enabled);
            Assert.IsTrue(background.enabled);
            Assert.GreaterOrEqual(label.fontSize, 34);
            Assert.That(label.text, Does.Contain("MARA VENN"));
            Assert.Greater(background.color.a, 0.75f,
                "bright arena surfaces need a deliberately opaque subtitle backing");
            Canvas.ForceUpdateCanvases();
            Assert.LessOrEqual(label.preferredHeight, label.rectTransform.rect.height + 0.5f,
                "the complete localized subtitle must fit rather than truncate a wrapped final word");
        }

        private static T Private<T>(object target, string field) where T : Object
        {
            FieldInfo? info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            T? value = info!.GetValue(target) as T;
            Assert.IsNotNull(value, field);
            return value!;
        }
    }
}
