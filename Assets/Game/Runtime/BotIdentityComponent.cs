using System;
using UnityEngine;

namespace LocalTanks
{
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
