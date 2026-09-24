using System.Collections.Concurrent;

// Minimal game boundary for integration tests. Database, HTTP, and world
// operations are the actual production source files linked by the test project.
namespace Microsoft.Xna.Framework
{
    public struct Color { public static Color Red => default; }
}
namespace Terraria.ID
{
    public static class NetmodeID { public const int SinglePlayer = 0; }
}
namespace Terraria
{
    public sealed class WorldFileData
    {
        public Guid UniqueId { get; set; }
        public string Path { get; set; }
        public bool IsCloudSave { get; set; }
    }
    public static class Main
    {
        public static string SavePath, worldName;
        public static WorldFileData ActiveWorldFileData;
        public static bool gameMenu, dedServ;
        public static int netMode;
        public static readonly BlockingCollection<Action> Queue = new();
        public static void QueueMainThreadAction(Action action) => Queue.Add(action);
        public static void NewText(string text, Microsoft.Xna.Framework.Color color = default) { }
    }
    public static class WorldGen
    {
        public static void SaveAndQuit(Action callback) { Main.gameMenu = true; callback(); }
        public static void playWorld() { Main.gameMenu = false; }
    }
}
