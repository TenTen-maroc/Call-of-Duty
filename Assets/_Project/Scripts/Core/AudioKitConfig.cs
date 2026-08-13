#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Optional authored sound library. Every consumer keeps its generated WAV
    /// or silent fallback; assigning this kit replaces only presentation data.
    /// A partial library is rejected so a source cannot quietly leave half of
    /// the arena sounding imported and half sounding provisional.
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

        public bool HasNoAssignments => AssignedCount == 0;
        public bool HasCompleteAssignments => AssignedCount == ExpectedAssignmentCount;
        public bool IsValid => HasNoAssignments || HasCompleteAssignments;

        public const int ExpectedAssignmentCount = 18;

        private int AssignedCount =>
            Count(footstepConcreteA) + Count(footstepConcreteB) +
            Count(footstepConcreteC) + Count(footstepConcreteD) +
            Count(impactConcrete) + Count(impactMetal) + Count(impactGrate) + Count(impactFlesh) +
            Count(roomTone) + Count(ventLoop) + Count(powerLoop) +
            Count(droneAlert) + Count(droneShot) + Count(slamWindup) +
            Count(explosion) + Count(droneDeath) + Count(confirm) + Count(refused);

        private static int Count(Object? asset) => asset == null ? 0 : 1;
    }
}
