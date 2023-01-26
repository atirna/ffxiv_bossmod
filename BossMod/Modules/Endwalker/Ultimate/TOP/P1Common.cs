﻿using System.Collections.Generic;

namespace BossMod.Endwalker.Ultimate.TOP
{
    // common assignments for program loop & pantokrator
    class P1Common : BossComponent
    {
        protected struct PlayerState
        {
            public int Order;
            public int Group; // 0 if unassigned, otherwise 1 or 2
        }

        protected PlayerState[] PlayerStates = new PlayerState[PartyState.MaxPartySize];
        private int _numOrdersAssigned;

        public override void AddHints(BossModule module, int slot, Actor actor, TextHints hints, MovementHints? movementHints)
        {
            var ps = PlayerStates[slot];
            if (ps.Order != 0)
                hints.Add($"Order: {ps.Order}, group: {ps.Group}", false);
        }

        public override PlayerPriority CalcPriority(BossModule module, int pcSlot, Actor pc, int playerSlot, Actor player, ref uint customColor)
        {
            var playerOrder = PlayerStates[playerSlot].Order;
            return playerOrder == 0 ? PlayerPriority.Irrelevant : playerOrder == PlayerStates[pcSlot].Order ? PlayerPriority.Danger : PlayerPriority.Normal;
        }

        public override void OnStatusGain(BossModule module, Actor actor, ActorStatus status)
        {
            int order = (SID)status.ID switch
            {
                SID.InLine1 => 1,
                SID.InLine2 => 2,
                SID.InLine3 => 3,
                SID.InLine4 => 4,
                _ => 0
            };
            if (order > 0)
            {
                var slot = module.Raid.FindSlot(actor.InstanceID);
                if (slot >= 0)
                    PlayerStates[slot].Order = order;
                if (++_numOrdersAssigned == PartyState.MaxPartySize)
                    InitAssignments(module);
            }
        }

        private void InitAssignments(BossModule module)
        {
            List<(int slot, int group, int priority, int order)> assignments = new();
            foreach (var a in Service.Config.Get<TOPConfig>().ProgramLoopPantokratorAssignments.Resolve(module.Raid))
                assignments.Add((a.slot, a.group >> 2, a.group & 3, PlayerStates[a.slot].Order));
            if (assignments.Count == 0)
                return; // invalid assignments

            assignments.SortBy(a => a.order);
            for (int i = 0; i < assignments.Count; i += 2)
            {
                var a1 = assignments[i];
                var a2 = assignments[i + 1];
                if (a1.group == a2.group)
                {
                    if (a1.priority > a2.priority)
                        a1.group = a1.group == 0 ? 1 : 0;
                    else
                        a2.group = a2.group == 0 ? 1 : 0;
                }
                PlayerStates[a1.slot].Group = a1.group + 1;
                PlayerStates[a2.slot].Group = a2.group + 1;
            }
        }
    }
}
