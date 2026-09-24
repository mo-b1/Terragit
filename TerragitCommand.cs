using System;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace Terragit;

public class TerragitCommand : ModCommand
{
    public override string Command => "tg";
    public override CommandType Type => CommandType.Chat;
    public override string Description => "tg init, commit, pull, checkout, diff, web";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("Usage: /tg init|commit|pull|checkout|diff|web");
            string branch = args.Length > 1 ? args[1] : null;
            switch (args[0])
            {
                case "web":
                    caller.Reply(Terragit.WebUrl == null
                        ? "[Terragit] Website unavailable. Check the tModLoader log."
                        : "[Terragit] Open " + Terragit.WebUrl, Color.LightBlue);
                    return;
                case "init":
                    WorldOperations.Init();
                    caller.Reply("[Terragit] Repository ready, branch: " + TDatabase.GetCurrentRepo().CurrentBranch, Color.Green);
                    return;
                case "commit":
                    if (args.Length < 2) throw new ArgumentException("Usage: /tg commit \"message\"");
                    WorldOperations.Commit(WorldOperations.ActiveRepositoryId, string.Join(" ", args, 1, args.Length - 1));
                    caller.Reply("[Terragit] Commit created and local branch reference updated.", Color.Green);
                    return;
                case "pull":
                case "checkout":
                    WorldOperations.Restore(WorldOperations.ActiveRepositoryId, branch, args[0] == "checkout");
                    caller.Reply("[Terragit] Saving and reloading the world. Restore status is available on the website.", Color.Green);
                    return;
                case "diff":
                    WorldDiff diff = WorldOperations.Diff(WorldOperations.ActiveRepositoryId, branch);
                    caller.Reply(diff.Matches ? "[Terragit] No differences — saved world matches latest commit."
                        : $"[Terragit] Differences: {diff.CurrentSize} bytes saved / {diff.CommitSize} bytes committed ({diff.CurrentSize - diff.CommitSize:+#;-#;0} bytes).", Color.LightBlue);
                    return;
                default: throw new ArgumentException("Unknown command: " + args[0]);
            }
        }
        catch (Exception ex) { caller.Reply("[Terragit] " + ex.Message, Color.Red); }
    }
}
    
