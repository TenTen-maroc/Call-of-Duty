#nullable enable
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoD.Core
{
    /// <summary>
    /// 00_Boot initialises core systems and hands off to the first real scene.
    ///
    /// The important half of this convention is the other direction: pressing
    /// Play in whatever scene you are working on must still work. Losing that
    /// costs more time than anything else on a solo project, so nothing here is
    /// allowed to become mandatory setup that other scenes depend on.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BootLoader : MonoBehaviour
    {
        [Tooltip("Scene to load once boot-time systems exist. Must be in Build Settings.")]
        [SerializeField] private string _firstScene = "10_GreyBox";

        private void Start()
        {
            if (string.IsNullOrWhiteSpace(_firstScene))
            {
                GameLog.Error("BootLoader has no first scene set.", this);
                return;
            }

            GameLog.Info("Boot -> " + _firstScene, this);
            SceneManager.LoadScene(_firstScene);
        }
    }
}
