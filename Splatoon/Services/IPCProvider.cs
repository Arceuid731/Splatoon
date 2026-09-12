using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.EzIpcManager;
using ECommons.GameFunctions;
using Splatoon.Gui.Priority;
using Splatoon.SplatoonScripting.Priority;
using System;
using System.Collections.Generic;
using System.Text;

namespace Splatoon.Services;

internal class IPCProvider
{
    private IPCProvider()
    {
        EzIPC.Init(this);
    }

    [EzIPC]
    public unsafe RolePosition GetRoleOf(IPlayerCharacter pc)
    {
        if(P.PriorityPopupWindow?.Assignments != null)
        {
            for(var i = 0; i < P.PriorityPopupWindow.Assignments.Count; i++)
            {
                var ass = P.PriorityPopupWindow.Assignments[i];
                if(ass.IsInParty(false, out var m) && m.IGameObject != null && m.IGameObject.AddressEquals(pc))
                {
                    return PriorityPopupWindow.RolePositions.SafeSelect(i);
                }
            }
        }
        return RolePosition.Not_Selected;
    }
}
