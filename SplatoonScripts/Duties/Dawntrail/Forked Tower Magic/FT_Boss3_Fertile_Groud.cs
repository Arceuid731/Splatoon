using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameFunctions.VirtualTableClassifier;
using ECommons.Hooks.ActionEffectTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using Splatoon.Memory;
using Splatoon.SplatoonScripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SplatoonScriptsOfficial.Duties.Dawntrail.Forked_Tower_Magic;

public class FT_Boss3_Fertile_Groud : SplatoonScript<FT_Boss3_Fertile_Groud.Config>
{
    public override Metadata Metadata { get; } = new(1, "NightmareXIV");
    public override HashSet<uint>? ValidTerritories { get; } = [1346];

    uint DebuffRed = 5137;
    uint DebuffBlue = 5136;

    string BlueLeft = "vfx/common/eff/m0475_stlp_ab_c0x.avfx";
    string BlueRight = "vfx/common/eff/m0475_stlp_ba_c0x.avfx";

    int AttackCnt = 0;

    public override void OnSetup()
    {
        Controller.RegisterElementsFromMultilineCode("""
            {"Name":"UnsafeLeft","type":4,"refY":20.0,"offY":17.0,"radius":30.0,"coneAngleMin":180,"coneAngleMax":360,"fillIntensity":0.5,"refActorObjectID":1073784007,"refActorComparisonType":2,"includeRotation":true}
            {"Name":"UnsafeRight","type":4,"refY":20.0,"offY":17.0,"radius":30.0,"coneAngleMax":180,"fillIntensity":0.5,"refActorObjectID":1073784007,"refActorComparisonType":2,"includeRotation":true}
            {"Name":"UnsafeLeftNext","type":4,"refY":20.0,"offY":17.0,"radius":30.0,"coneAngleMin":180,"coneAngleMax":360,"color":3355508725,"fillIntensity":0.3,"refActorObjectID":1073784007,"refActorComparisonType":2,"includeRotation":true}
            {"Name":"UnsafeRightNext","type":4,"refY":20.0,"offY":17.0,"radius":30.0,"coneAngleMax":180,"color":3355508725,"fillIntensity":0.3,"refActorObjectID":1073784007,"refActorComparisonType":2,"includeRotation":true}
            """);
    }

    public override void OnReset()
    {
        AttackCnt = 0;
    }

    public override void OnActionEffectEvent(ActionEffectSet set)
    {
        if(set.Action?.RowId.EqualsAny(47574u, 47516u) == true)
        {
            AttackCnt++;
            EzThrottler.Throttle("Hide", 1000, true);
            if(IsBlue != null) IsBlue = !IsBlue;
        }
    }

    public override void OnGainBuffEffect(uint sourceId, Status Status)
    {
        if(sourceId.GetObject()?.AddressEquals(BasePlayer) == true)
        {
            if(((uint)Status.StatusId).EqualsAny(DebuffBlue, DebuffRed))
            {
                EzThrottler.Reset("Hide");
            }
        }
    }

    bool? IsBlue = null;

    public override void OnUpdate()
    {
        Controller.Hide();
        if(EzThrottler.Check("Hide"))
        {
            IsBlue = null;
            if(BasePlayer.HasStatus(DebuffBlue)) IsBlue = true;
            if(BasePlayer.HasStatus(DebuffRed)) IsBlue = false;
        }
        if(IsBlue != null) 
        {
            var npcs = Svc.Objects.OfTypeIBattleNpc().Where(x => x.DataId == 19432).Where(x => IsBlueLeft(x).Item1 != null).OrderByDescending(x => IsBlueLeft(x).Item2).ToList();
            if(AttackCnt < npcs.Count)
            {
                var currentNpc = npcs[AttackCnt];
                var e = Controller.GetElementByName($"Unsafe{(IsBlueLeft(currentNpc).Item1 == IsBlue ? "Left" : "Right")}")!;
                e.Enabled = true;
                e.refActorObjectID = currentNpc.ObjectId;
            }
            if(C.ShowNext && AttackCnt + 1 < npcs.Count)
            {
                var nextNpc = npcs[AttackCnt + 1];
                var e = Controller.GetElementByName($"Unsafe{(IsBlueLeft(nextNpc).Item1 != IsBlue ? "Left" : "Right")}Next")!;
                e.Enabled = true;
                e.refActorObjectID = nextNpc.ObjectId;
            }
        }
    }

    (bool?, float) IsBlueLeft(IBattleNpc b)
    {
        if(AttachedInfo.TryGetVfx(b, out var vfx))
        {
            var f = vfx?.Where(x => x.Key.EqualsAny(BlueLeft, BlueRight)).OrderBy(x => x.Value.AgeF).FirstOrNull();
            if(f != null)
            {
                return (f.Value.Key == BlueLeft, f.Value.Value.AgeF);
            }
        }
        return default;
    }

    public override void OnSettingsDraw()
    {
        ImGui.Checkbox("Show next", ref C.ShowNext);
        if(ImGui.CollapsingHeader("Debug"))
        {
            foreach(var x in Svc.Objects.OfTypeIBattleNpc().Where(x => x.DataId == 19432))
            {
                ImGui.Text($"Object ID: {x.ObjectId}, Data ID: {x.DataId}, IsBlueLeft: {IsBlueLeft(x)}");
            }
        }
    }

    public class Config
    {
        public bool ShowNext = false;
    }
}
