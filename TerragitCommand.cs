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

    private static HttpServer _httpServer;

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
        }
        else if (args.Length == 1)
        {
            _commands[args[0]](caller, args);
        }
    }

    // Creates an empty repo in the database
    public static void OnInit(CommandCaller caller, string[] args)
    {
        Main.NewText("yo");
        if (Database.CreateRepo()){
            caller.Reply("Repository created succesfully, Branch: " + Database.DefaultBranch);
            return;
        } 
        caller.Reply("Repository already exists", new Color(1.0f, 0.0f, 0.0f));

    }

    public static void OnAdd(CommandCaller caller, string[] args)
    {
        
    }
    public static void OnCommit(CommandCaller caller, string[] args){}
    public static void OnPull(CommandCaller caller, string[] args){}
    public static void OnDiff(CommandCaller caller, string[] args){}
    public static void OnPush(CommandCaller caller,string[] args){}


    private static void StartServer(CommandCaller caller)
    {
        _httpServer = new HttpServer();
        _httpServer.Start();
    }
    
}