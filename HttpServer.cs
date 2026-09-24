using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Terraria;

namespace Terragit;

public sealed class HttpServer : IDisposable
{
    private HttpListener listener;
    private readonly CancellationTokenSource stopping = new();
    private readonly Action<string> log;
    private readonly Func<string, byte[]> readAsset;
    private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private Task serverTask;
    public string Url { get; private set; }

    public HttpServer(Action<string> log, Func<string, byte[]> readAsset = null)
    {
        this.log = log;
        this.readAsset = readAsset ?? ReadEmbeddedAsset;
    }

    private static byte[] ReadEmbeddedAsset(string file)
    {
        using Stream stream = typeof(HttpServer).Assembly.GetManifestResourceStream("Terragit.Web." + file)
            ?? throw new InvalidOperationException("Website asset missing: " + file);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public void Start()
    {
        if (serverTask != null) throw new InvalidOperationException("The website is already running.");
        // Use explicit loopback prefixes, never a wildcard or LAN interface.
        for (int port = 17840; port < 17850; port++)
        {
            Url = $"http://127.0.0.1:{port}/";
            // A failed Start disposes HttpListener on some platforms.
            listener = new HttpListener();
            listener.Prefixes.Add(Url);
            try { listener.Start(); break; }
            catch (HttpListenerException) when (port < 17849) { listener.Close(); }
        }
        serverTask = Task.Run(Listen);
    }

    private async Task Listen()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                HttpListenerContext context = await listener.GetContextAsync();
                // Handle one request at a time; every request has a timeout.
                await Handle(context);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
            if (!stopping.IsCancellationRequested) log("Website stopped: " + ex.Message);
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            // Invalid requests may have unread bodies; do not reuse that connection.
            response.KeepAlive = false;
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["X-Frame-Options"] = "DENY";
            response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            if (!IPAddress.IsLoopback(request.RemoteEndPoint.Address)
                || request.Headers["Host"] != new Uri(Url).Authority
                || (request.Headers["Origin"] != null && request.Headers["Origin"] != Url.TrimEnd('/'))
                || request.Headers["Sec-Fetch-Site"] == "cross-site")
            {
                response.StatusCode = 403;
                await SendJson(response, new { error = "Only same-origin local requests are allowed." }, deadline.Token);
                return;
            }

            string path = request.Url.AbsolutePath;
            if (request.HttpMethod == "GET" && (path == "/" || path == "/app.js" || path == "/style.css" || path == "/logo.png"))
            {
                string file = path == "/" ? "index.html" : path.Substring(1);
                string contentType = file.EndsWith(".html") ? "text/html; charset=utf-8"
                    : file.EndsWith(".js") ? "text/javascript; charset=utf-8"
                    : file.EndsWith(".png") ? "image/png" : "text/css; charset=utf-8";
                // tModLoader converts packaged .png files into game textures.
                // Keep a byte-for-byte .bin copy for browser image responses.
                await Send(response, readAsset(file == "logo.png" ? "logo.png.bin" : file), contentType, deadline.Token);
            }
            else if (request.HttpMethod == "GET" && path == "/api/state")
            {
                byte[] state = await OnGameThread(() => JsonSerializer.SerializeToUtf8Bytes(new
                {
                    token,
                    activeRepositoryId = WorldOperations.ActiveRepositoryId,
                    activeWorld = WorldOperations.HasLocalWorld ? Main.worldName : null,
                    restoreInProgress = WorldOperations.RestoreInProgress,
                    restoreStatus = WorldOperations.RestoreStatus,
                    repositories = TDatabase.GetRepositories().Select(r => new
                    {
                        r.Guid, r.Name, r.CurrentBranch, r.Branches,
                        commits = r.Commits.Select(c => new { c.CommitHash, c.Message, c.CommitTimestamp, c.PreviousCommitHash })
                    })
                }, json), deadline.Token);
                await Send(response, state, "application/json; charset=utf-8", deadline.Token);
            }
            else if (request.HttpMethod == "GET" && path == "/api/snapshot")
            {
                string id = request.QueryString["repo"], hash = request.QueryString["hash"];
                byte[] bytes = await OnGameThread(() => File.ReadAllBytes(TDatabase.GetSnapshotPath(id, hash)), deadline.Token);
                // Old snapshots contain no extension metadata; keep their actual .bin format.
                response.Headers["Content-Disposition"] = $"attachment; filename=\"{hash}.bin\"";
                await Send(response, bytes, "application/octet-stream", deadline.Token);
            }
            else if (request.HttpMethod == "POST" && path == "/api/action")
            {
                if (request.Headers["X-Terragit-Token"] != token)
                {
                    response.StatusCode = 403;
                    await SendJson(response, new { error = "Session expired. Refresh the page." }, deadline.Token);
                    return;
                }
                if (request.ContentLength64 < 0 || request.ContentLength64 > 8192)
                {
                    response.StatusCode = 413;
                    await SendJson(response, new { error = "Request body must be at most 8 KB." }, deadline.Token);
                    return;
                }
                if (!(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    response.StatusCode = 415;
                    await SendJson(response, new { error = "Expected JSON." }, deadline.Token);
                    return;
                }
                var action = await JsonSerializer.DeserializeAsync<WebAction>(request.InputStream, json, deadline.Token)
                    ?? throw new ArgumentException("Missing action.");
                byte[] result = await OnGameThread(() => JsonSerializer.SerializeToUtf8Bytes(Execute(action), json), deadline.Token);
                await Send(response, result, "application/json; charset=utf-8", deadline.Token);
            }
            else
            {
                response.StatusCode = 404;
                await SendJson(response, new { error = "Not found." }, deadline.Token);
            }
        }
        catch (Exception ex)
        {
            try
            {
                response.StatusCode = ex is OperationCanceledException ? 503
                    : ex is ArgumentException or InvalidOperationException or JsonException ? 400 : 500;
                if (response.StatusCode == 500) log("Website request failed: " + ex);
                await SendJson(response, new { error = response.StatusCode == 503
                    ? "The game is busy or closing. Return to tModLoader, then refresh."
                    : response.StatusCode == 500 ? "The operation failed. Check the tModLoader log." : ex.Message }, stopping.Token);
            }
            catch (Exception sendError) when (sendError is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException) { }
        }
        finally { response.Close(); }
    }

    private object Execute(WebAction action)
    {
        if (WorldOperations.RestoreInProgress)
            throw new InvalidOperationException("Wait for the world restore to finish.");
        switch (action.Action)
        {
            case "init":
                WorldOperations.Init();
                break;
            case "rename":
                TDatabase.RenameRepository(action.RepoId, action.Name);
                break;
            case "branch":
                TDatabase.CreateBranch(action.RepoId, action.Name, action.Branch);
                break;
            case "deleteBranch":
                TDatabase.DeleteBranch(action.RepoId, action.Branch);
                break;
            case "message":
                TDatabase.UpdateCommitMessage(action.RepoId, action.Hash, action.Message);
                break;
            case "commit":
                WorldOperations.RequireWorld(action.RepoId ?? throw new ArgumentException("Repository required."));
                WorldOperations.Commit(action.RepoId, action.Message);
                break;
            case "diff":
                WorldOperations.RequireWorld(action.RepoId ?? throw new ArgumentException("Repository required."));
                return WorldOperations.Diff(action.RepoId, action.Branch);
            case "pull":
            case "checkout":
            case "restore":
                WorldOperations.RequireWorld(action.RepoId ?? throw new ArgumentException("Repository required."));
                if (action.Action == "restore" && string.IsNullOrEmpty(action.Hash))
                    throw new ArgumentException("Choose a commit to restore.");
                WorldOperations.Restore(action.RepoId, action.Branch, action.Action == "checkout",
                    action.Action == "restore" ? action.Hash : null);
                return new { message = "Restore started. The world will save, back up, and reload." };
            default: throw new ArgumentException("Unknown action.");
        }
        return new { message = "Changes saved." };
    }

    private static async Task<T> OnGameThread<T>(Func<T> work, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        object gate = new();
        // Cancellation and execution share a gate: a timed-out queued mutation
        // must never execute later when the game becomes responsive again.
        using var registration = cancellation.Register(() =>
        {
            lock (gate) completion.TrySetCanceled(cancellation);
        });
        Main.QueueMainThreadAction(() =>
        {
            lock (gate)
            {
                if (completion.Task.IsCompleted) return;
                try { completion.TrySetResult(work()); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }
        });
        return await completion.Task;
    }

    private Task SendJson(HttpListenerResponse response, object value, CancellationToken cancellation) =>
        Send(response, JsonSerializer.SerializeToUtf8Bytes(value, json), "application/json; charset=utf-8", cancellation);

    private static async Task Send(HttpListenerResponse response, byte[] bytes, string contentType, CancellationToken cancellation)
    {
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellation);
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener?.Close();
        // Do not block the game thread waiting for queued requests during unload.
    }
}

public sealed class WebAction
{
    public string Action { get; set; }
    public string RepoId { get; set; }
    public string Name { get; set; }
    public string Branch { get; set; }
    public string Hash { get; set; }
    public string Message { get; set; }
}
