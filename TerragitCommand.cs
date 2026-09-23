using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace Terragit;

// This class uses the server to interact with the sqlite database

public class TerragitCommand : ModCommand
{
    public override string Command => "tg";
    public override CommandType Type => CommandType.Chat;
    public override string Description => "tg init, tg add, tg commit, tg push, tg pull, tg diff";

    private static readonly Dictionary<string, Action<CommandCaller, string[]>> _commands = new Dictionary<string, Action<CommandCaller, string[]>>
    {
        ["init"] = OnInit,
        ["add"] = OnAdd,
        ["commit"] = OnCommit,
        ["push"] = OnPush,
        ["pull"] = OnPull,
        ["diff"] = OnDiff
    };

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (args.Length == 0)
        {
            caller.Reply("Invalid command, see help", new Color(1.0f,0.0f,0.0f));
            return;
        }

        if (_commands.TryGetValue(args[0], out var handler))
        {
            handler(caller, args);
        }
        else
        {
            caller.Reply("Unknown command: " + args[0], new Color(1.0f, 0.0f, 0.0f));
        }
    }

    // Creates an empty repo in the database
    public static void OnInit(CommandCaller caller, string[] args)
    {
        Repository r = TDatabase.Init();
        if (r != null)
        {
            caller.Reply("Repository created succesfully, Branch: " + r.CurrentBranch);
        } else
        {
            caller.Reply("Repository already exists", new Color(1.0f, 0.0f, 0.0f));
        }
    }

    public static void OnAdd(CommandCaller caller, string[] args)
    {
        caller.Reply("[Terragit] Staging world ready for commit...", new Color(0.0f, 1.0f, 0.0f));

        string currentWorldPath = GetWorldFilePath();
        if (currentWorldPath == null)
        {
            caller.Reply("World file doesn't exist in storage", new Color(1.0f, 0.0f, 0.0f));
            return;
        }

        TDatabase.currentWorldData = File.ReadAllBytes(currentWorldPath);
        caller.Reply("[Terragit] World staged successfully", new Color(0.0f, 1.0f, 0.0f));
    }

    private static string GetWorldFilePath()
    {
        string path = Path.Combine(Main.WorldPath, Main.worldName + ".wld");
        if (File.Exists(path)) return path;
        
        path = Path.Combine(Main.WorldPath, Main.worldName + ".twld");
        if (File.Exists(path)) return path;
        
        return null;
    }
    public static void OnCommit(CommandCaller caller, string[] args)
    {
        caller.Reply("[Terragit] A commit request has been received", new Color(0.0f, 1.0f, 0.0f));

        if (args.Length < 2)
        {
            caller.Reply("Please add a comment: /tg commit \"added a block\"");
            return;
        }

        if (TDatabase.currentWorldData == null)
        {
            caller.Reply("[Terragit] You need to add the world first via /tg add");
            return;
        }

        string message = string.Join(" ", args, 1, args.Length - 1);
        try
        {
            TDatabase.CreateCommit(message);
            caller.Reply("[Terragit] Commit created successfully", new Color(0.0f, 1.0f, 0.0f));
        }
        catch (Exception ex)
        {
            caller.Reply("[Terragit] Commit failed: " + ex.Message, new Color(1.0f, 0.0f, 0.0f));
        }
    }

    public static void OnPull(CommandCaller caller, string[] args)
    {
        string pathToWorldFile;
        
        if (args.Length < 2)
        {
            pathToWorldFile = TDatabase.GetPushedCommitPath() ?? TDatabase.GetLatestCommitPath();
        }
        else
        {
            Branch branch = TDatabase.GetBranch(args[1]);
            if (branch == null)
            {
                caller.Reply("Branch not found: " + args[1], new Color(1.0f, 0.0f, 0.0f));
                return;
            }
            pathToWorldFile = TDatabase.GetPushedCommitPath(branch.Name) ?? TDatabase.GetLatestCommitPath(branch.Name);
        }

        if (pathToWorldFile == null || !File.Exists(pathToWorldFile))
        {
            caller.Reply("No commit found to pull", new Color(1.0f, 0.0f, 0.0f));
            return;
        }

        string currentWorldPath = GetWorldFilePath();
        if (currentWorldPath == null)
        {
            caller.Reply("Current world file not found", new Color(1.0f, 0.0f, 0.0f));
            return;
        }
        try
        {
            File.Copy(pathToWorldFile, currentWorldPath, true);
        }
        catch (Exception ex)
        {
            caller.Reply("[Terragit] Pull failed: " + ex.Message, new Color(1.0f, 0.0f, 0.0f));
            return;
        }
        
        caller.Reply("[Terragit] Pulled latest commit successfully", new Color(0.0f, 1.0f, 0.0f));
        Main.NewText("[Terragit] World reloaded from commit", Color.Green);
    }

    public static void OnDiff(CommandCaller caller, string[] args)
    {
        string pathToWorldFile;
        
        if (args.Length < 2)
        {
            pathToWorldFile = TDatabase.GetLatestCommitPath();
        }
        else
        {
            Branch branch = TDatabase.GetBranch(args[1]);
            if (branch == null)
            {
                caller.Reply("Branch not found: " + args[1], new Color(1.0f, 0.0f, 0.0f));
                return;
            }
            pathToWorldFile = TDatabase.GetLatestCommitPath(branch.Name);
        }

        if (pathToWorldFile == null || !File.Exists(pathToWorldFile))
        {
            caller.Reply("No commit found to diff against", new Color(1.0f, 0.0f, 0.0f));
            return;
        }

        string currentWorldPath = GetWorldFilePath();
        if (currentWorldPath == null)
        {
            caller.Reply("Current world file doesn't exist", new Color(1.0f, 0.0f, 0.0f));
            return;
        }

        byte[] currentData = File.ReadAllBytes(currentWorldPath);
        byte[] commitData = File.ReadAllBytes(pathToWorldFile);

        string currentHash = Utils.Hash(currentData);
        string commitHash = Utils.Hash(commitData);

        if (currentHash == commitHash)
        {
            caller.Reply("[Terragit] No differences - world matches latest commit", new Color(0.0f, 1.0f, 1.0f));
            return;
        }

        caller.Reply("[Terragit] Differences found:", new Color(1.0f, 1.0f, 0.0f));
        caller.Reply($"  Current world: {currentData.Length} bytes (hash: {currentHash.Substring(0, 8)}...)", Color.White);
        caller.Reply($"  Latest commit: {commitData.Length} bytes (hash: {commitHash.Substring(0, 8)}...)", Color.White);
        caller.Reply($"  Size difference: {currentData.Length - commitData.Length} bytes", Color.White);
    }

    public static void OnPush(CommandCaller caller, string[] args)
    {
        Repository repo = TDatabase.GetCurrentRepo();
        if (repo == null)
        {
            caller.Reply("No repository found. Run '/tg init' first.", new Color(1.0f, 0.0f, 0.0f));
            return;
        }

        string path = TDatabase.GetLatestCommitPath();
        if (path == null || !File.Exists(path))
        {
            caller.Reply("[Terragit] Nothing to push. Create a commit first.", new Color(1.0f, 0.0f, 0.0f));
            return;
        }

        if (TDatabase.PushCurrentBranch() == null)
        {
            caller.Reply("[Terragit] Push failed.", new Color(1.0f, 0.0f, 0.0f));
            return;
        }

        caller.Reply("[Terragit] Pushed commit " + Path.GetFileNameWithoutExtension(path), new Color(0.0f, 1.0f, 0.0f));
    }

}
    
