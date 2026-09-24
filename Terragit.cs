using System;
using Terraria.ModLoader;

namespace Terragit;

public class Terragit : Mod
{
    private HttpServer webServer;
    public static string WebUrl { get; private set; }

    public override void Load()
    {
        TDatabase.Load();
        WorldOperations.Reset();
        try
        {
            webServer = new HttpServer(message => Logger.Warn(message), file => GetFileBytes("Web/" + file));
            webServer.Start();
            WebUrl = webServer.Url;
            Logger.Info("Terragit website: " + WebUrl);
        }
        catch (Exception ex)
        {
            webServer?.Dispose();
            webServer = null;
            Logger.Warn("Terragit website could not start: " + ex.Message);
        }
    }

    public override void Unload()
    {
        webServer?.Dispose();
        webServer = null;
        WebUrl = null;
        WorldOperations.Reset();
    }
}
