using Dalamud.Bindings.ImGui;
using Splatoon.Gui.PresetHub;
using System.Numerics;

unsafe
{
    var context = ImGui.CreateContext();
    try
    {
        var io = ImGui.GetIO();
        io.IniFilename = null;
        io.DisplaySize = new Vector2(1600, 1200);
        io.DeltaTime = 1f / 60;
        io.Fonts.AddFontDefault();
        byte* pixels;
        int width, height;
        io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
        ImGui.NewFrame();
        ImGui.SetNextWindowSize(new Vector2(1200, 1000));
        ImGui.Begin("Coverage regression");
        if(args.Contains("--reproduce-empty-label"))
        {
            // Run only in this isolated process: this reproduces the original native crash.
            ImGui.TreeNodeEx("", ImGuiTreeNodeFlags.DefaultOpen);
            throw new Exception("The original empty-label call unexpectedly survived.");
        }
        var labels = new string?[] { null, "", " ", "A Realm Reborn", "Other areas", "Donjons", "迷宮", "100% covered", "Name##suffix" };
        foreach(var (label, index) in labels.Select((label, index) => (label, index)))
        {
            if(!CoverageTree.Node($"area-{index}", label, ImGuiTreeNodeFlags.DefaultOpen)) throw new Exception("Area did not expand.");
            if(!CoverageTree.Node("category", label, ImGuiTreeNodeFlags.DefaultOpen)) throw new Exception("Category did not expand.");
            if(!CoverageTree.Node("encounter", "Boss (2/3)", ImGuiTreeNodeFlags.DefaultOpen)) throw new Exception("Encounter did not expand.");
            ImGui.TextUnformatted("Mechanic");
            ImGui.TreePop();
            ImGui.TreePop();
            ImGui.TreePop();
        }
        ImGui.End();
        ImGui.Render();
        if(ImGui.GetDrawData().CmdListsCount == 0) throw new Exception("No native draw commands generated.");
        Console.WriteLine($"PASS: {labels.Length} nested coverage trees rendered with the real Dalamud ImGui binding.");
    }
    finally { ImGui.DestroyContext(context); }
}
