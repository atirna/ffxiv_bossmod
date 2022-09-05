﻿using System.Collections.Generic;
using System.Linq;

namespace BossMod.Endwalker.Extreme.Ex4Barbariccia
{
    class HairFlayUpbraid : BossComponent
    {
        public enum PlayerState { None, Spread, Enumeration }

        public int NumActiveMechanics { get; private set; }
        private PlayerState[] _states = new PlayerState[PartyState.MaxPartySize];

        private const float _enumRadius = 3;
        private const float _spreadRadius = 10;

        public override void AddHints(BossModule module, int slot, Actor actor, TextHints hints, MovementHints? movementHints)
        {
            if (NumActiveMechanics == 0)
                return;

            if (_states[slot] == PlayerState.Spread)
            {
                // check only own circle - no one should be inside, this automatically resolves mechanic for us
                if (module.Raid.WithoutSlot().InRadiusExcluding(actor, _spreadRadius).Any())
                    hints.Add("Spread!");
            }
            else
            {
                // check that we're not clipped by circles and stacked otherwise
                if (PlayersWithState(module, PlayerState.Enumeration).InRadius(actor.Position, _enumRadius).Count() != 1)
                    hints.Add("Stack!");
                if (PlayersWithState(module, PlayerState.Spread).InRadius(actor.Position, _spreadRadius).Any())
                    hints.Add("GTFO from spread markers!");
            }
        }

        public override PlayerPriority CalcPriority(BossModule module, int pcSlot, Actor pc, int playerSlot, Actor player, ref uint customColor)
        {
            return NumActiveMechanics == 0 ? PlayerPriority.Irrelevant : _states[playerSlot] == PlayerState.Spread ? PlayerPriority.Danger : PlayerPriority.Normal;
        }

        public override void DrawArenaForeground(BossModule module, int pcSlot, Actor pc, MiniArena arena)
        {
            if (NumActiveMechanics == 0)
                return;

            if (_states[pcSlot] == PlayerState.Spread)
            {
                // draw only own circle - no one should be inside, this automatically resolves mechanic for us
                arena.AddCircle(pc.Position, _spreadRadius, ArenaColor.Danger);
            }
            else
            {
                // draw spread and stack circles
                foreach (var player in PlayersWithState(module, PlayerState.Enumeration))
                    arena.AddCircle(player.Position, _enumRadius, ArenaColor.Safe);
                foreach (var player in PlayersWithState(module, PlayerState.Spread))
                    arena.AddCircle(player.Position, _spreadRadius, ArenaColor.Danger);
            }
        }

        public override void OnCastStarted(BossModule module, Actor caster, ActorCastInfo spell)
        {
            var state = StateForSpell((AID)spell.Action.ID);
            if (state != PlayerState.None)
            {
                var slot = module.Raid.FindSlot(spell.TargetID);
                if (slot >= 0)
                {
                    _states[slot] = state;
                    ++NumActiveMechanics;
                }
            }
        }

        public override void OnCastFinished(BossModule module, Actor caster, ActorCastInfo spell)
        {
            var state = StateForSpell((AID)spell.Action.ID);
            if (state != PlayerState.None)
            {
                var slot = module.Raid.FindSlot(spell.TargetID);
                if (slot >= 0)
                {
                    _states[slot] = PlayerState.None;
                    --NumActiveMechanics;
                }
            }
        }

        private PlayerState StateForSpell(AID id) => id switch
        {
            AID.HairFlay => PlayerState.Spread,
            AID.Upbraid => PlayerState.Enumeration,
            _ => PlayerState.None,
        };

        private IEnumerable<Actor> PlayersWithState(BossModule module, PlayerState state)
        {
            foreach (var (slot, player) in module.Raid.WithSlot(true))
                if (_states[slot] == state)
                    yield return player;
        }
    }
}