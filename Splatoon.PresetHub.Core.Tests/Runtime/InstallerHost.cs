// Minimal host for testing the production installer without starting Dalamud.
using System.Text.Json;

namespace Splatoon
{
    internal sealed class Layout
    {
        public string Name = "";
        public string Group = "";
        public string PresetHubInstallationId = "";
        public bool Enabled = true;
        public HashSet<ushort> ZoneLockH = [];
        public List<Newtonsoft.Json.Linq.JObject> ElementsL = [];
    }
    internal static class P { internal static FakeConfig Config = new(); }
    internal static class Svc { internal static FakeClientState ClientState = new(); }
    internal sealed class FakeClientState { internal ushort TerritoryType = 387; }
    internal sealed class FakeConfig
    {
        internal List<Layout> LayoutsL = [];
        internal List<string> GroupOrder = [];
        internal HashSet<string> DisabledGroups = [];
        internal void Save() { }
    }
    internal static class CGui
    {
        internal static HashSet<string> OpenedGroup = [];
        internal static Layout? ScrollTo;
    }
    internal static class Logging { internal static void Log(this Exception exception) { } }
}
namespace Splatoon.ConfigGui.CGuiLayouts
{
    internal static class LayoutDrawSelector
    {
        internal static Layout? CurrentLayout;
        internal static object? CurrentElement;
    }
}
namespace ECommons
{
    internal sealed class TickScheduler { internal TickScheduler(Action action) => action(); }
    internal static class GenericHelpers { internal static void DeleteFileToRecycleBin(string path) { } }
}
namespace Splatoon.Utility
{
    internal static class Utils
    {
        internal static string Serialize(this Layout layout) => "~Lv2~" + Newtonsoft.Json.JsonConvert.SerializeObject(layout);
        internal static List<Layout> ImportLayouts(string content, bool silent, bool allowDuplicateNames)
        {
            Assert.False(allowDuplicateNames);
            using var payload = JsonDocument.Parse(content[5..]);
            var name = payload.RootElement.GetProperty("Name").GetString()!;
            if(name == "reject" || P.Config.LayoutsL.Any(x => x.Name == name)) return [];
            var layout = Newtonsoft.Json.JsonConvert.DeserializeObject<Layout>(content[5..])!;
            P.Config.LayoutsL.Add(layout);
            CGui.ScrollTo = layout;
            return [layout];
        }
    }
}
namespace Splatoon.SplatoonScripting
{
    internal static class ScriptingProcessor
    {
        internal static List<FakeScript> Scripts = [];
        internal static Action<string>? Loaded;
        internal static Action? Finished;
        internal static void CompileAndLoad(string code, string? path, bool first, bool ignoreCache,
            Action<string> loaded, Action finished) { Loaded = loaded; Finished = finished; }
        internal static void RemoveScript(FakeScript script) => Scripts.Remove(script);
    }
    internal sealed class FakeScript
    {
        internal FakeScriptData InternalData = new();
        internal void Disable() { }
        internal void UpdateState() { }
    }
    internal sealed class FakeScriptData
    {
        internal string FullName = "";
        internal string Path = "";
    }
}
