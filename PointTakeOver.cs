using System;
using Exiled.API.Features;
using Map = Exiled.Events.Handlers.Map;
using Player = Exiled.Events.Handlers.Player;
using Server = Exiled.Events.Handlers.Server;

namespace PointTakeOver
{
    public class PointTakeOver : Plugin<Config>
    {
        public override string Name => "PointTakeOver";
        public override string Author => "Cocoa";
        public override string Prefix => "PointTakeOver";
        public override Version Version { get; } = new(1, 0, 0);

        public static PointTakeOver Instance;

        public EventHandlers EventHandlers;

        public override void OnEnabled()
        {
            Instance = this;
            EventHandlers = new EventHandlers();
            RegisterEvents();
            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            UnregisterEvents();
            EventHandlers = null;
            Instance = null;
            base.OnDisabled();
        }

        private void RegisterEvents()
        {
            Map.Generated += EventHandlers.OnMapGenerated;
            Server.RoundStarted += EventHandlers.OnRoundStarted;
            Server.RestartingRound += EventHandlers.OnRoundRestarting;

            Player.Dying += EventHandlers.OnDying;
            Player.Died += EventHandlers.OnDied;
            Player.InteractingElevator += EventHandlers.OnInteractingElevator;
            Player.UsingRadioBattery += EventHandlers.OnUsingRadioBattery;
        }

        private void UnregisterEvents()
        {
            Map.Generated -= EventHandlers.OnMapGenerated;
            Server.RoundStarted -= EventHandlers.OnRoundStarted;
            Server.RestartingRound -= EventHandlers.OnRoundRestarting;

            Player.Dying -= EventHandlers.OnDying;
            Player.Died -= EventHandlers.OnDied;
            Player.InteractingElevator -= EventHandlers.OnInteractingElevator;
            Player.UsingRadioBattery -= EventHandlers.OnUsingRadioBattery;
        }
    }
}