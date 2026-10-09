using System;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class BotMentalState
    {
        [Range(0f, 1f)] public float morale = 0.65f;
        [Range(0f, 1f)] public float stress = 0.1f;
        [Range(0f, 1f)] public float boredom = 0.05f;
        [Range(0f, 1f)] public float planConfidence = 0.7f;
    }

    public sealed class BotIdentityComponent : MonoBehaviour
    {
        [SerializeField] private BotIdentity identity;
        [SerializeField] private BotMentalState mentalState = new BotMentalState();

        public BotIdentity Identity => identity;
        public BotMentalState MentalState => mentalState;

        public void Configure(BotIdentity value)
        {
            identity = value ?? throw new ArgumentNullException(nameof(value));
            mentalState = new BotMentalState();
        }
    }
}
