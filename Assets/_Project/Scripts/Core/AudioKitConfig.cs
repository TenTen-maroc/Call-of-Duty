#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Optional authored sound library. Every consumer keeps its generated WAV
    /// or silent fallback; assigning this kit replaces only presentation data.
    /// Each source section is independently all-null or all-complete, so source
    /// builders remain reproducible in either order without permitting a source
    /// to quietly leave half of its intended coverage provisional.
    /// </summary>
    [CreateAssetMenu(fileName = "Kit_Audio_", menuName = "CoD/Art/Audio Kit", order = 73)]
    public sealed class AudioKitConfig : ScriptableObject
    {
        [Header("Concrete footsteps — four prevents immediate repetition")]
        public AudioClip? footstepConcreteA;
        public AudioClip? footstepConcreteB;
        public AudioClip? footstepConcreteC;
        public AudioClip? footstepConcreteD;

        [Header("Bullet impacts")]
        public AudioClip? impactConcrete;
        public AudioClip? impactMetal;
        public AudioClip? impactGrate;
        public AudioClip? impactFlesh;

        [Header("Facility ambience")]
        public AudioClip? roomTone;
        public AudioClip? ventLoop;
        public AudioClip? powerLoop;

        [Header("Enemy and explosion cues")]
        public AudioClip? droneAlert;
        public AudioClip? droneShot;
        public AudioClip? slamWindup;
        public AudioClip? explosion;
        public AudioClip? droneDeath;

        [Header("Interface")]
        public AudioClip? confirm;
        public AudioClip? refused;

        [Header("Weapon recordings")]
        public AudioClip? rifleClose;
        public AudioClip? rifleTail;
        public AudioClip? rifleReload;

        public bool HasNoAssignments => HasNoKenneyAssignments && HasNoSonnissAssignments;
        public bool HasCompleteAssignments => HasKenneyAssignments && HasSonnissAssignments;
        public bool IsValid =>
            (HasNoKenneyAssignments || HasKenneyAssignments) &&
            (HasNoSonnissAssignments || HasSonnissAssignments);

        public bool HasNoKenneyAssignments => KenneyAssignedCount == 0;

        public bool HasKenneyAssignments =>
            footstepConcreteA != null && footstepConcreteB != null &&
            footstepConcreteC != null && footstepConcreteD != null &&
            impactConcrete != null && impactMetal != null && impactGrate != null && impactFlesh != null &&
            roomTone != null && ventLoop != null && powerLoop != null &&
            droneAlert != null && droneShot != null && slamWindup != null &&
            explosion != null && droneDeath != null && confirm != null && refused != null;

        public bool HasNoSonnissAssignments => SonnissAssignedCount == 0;
        public bool HasSonnissAssignments =>
            rifleClose != null && rifleTail != null && rifleReload != null;

        public const int KenneyAssignmentCount = 18;
        public const int SonnissAssignmentCount = 3;
        public const int ExpectedAssignmentCount = KenneyAssignmentCount + SonnissAssignmentCount;

        private int KenneyAssignedCount =>
            Count(footstepConcreteA) + Count(footstepConcreteB) +
            Count(footstepConcreteC) + Count(footstepConcreteD) +
            Count(impactConcrete) + Count(impactMetal) + Count(impactGrate) + Count(impactFlesh) +
            Count(roomTone) + Count(ventLoop) + Count(powerLoop) +
            Count(droneAlert) + Count(droneShot) + Count(slamWindup) +
            Count(explosion) + Count(droneDeath) + Count(confirm) + Count(refused);

        private int SonnissAssignedCount =>
            Count(rifleClose) + Count(rifleTail) + Count(rifleReload);

        private static int Count(Object? asset) => asset == null ? 0 : 1;
    }
}
