using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Terragit;
using Game = Terraria.Main;

string fixture = Path.Combine(Path.GetTempPath(), "terragit-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
Game.SavePath = fixture;
var worldA = new Terraria.WorldFileData { UniqueId = Guid.NewGuid(), Path = Path.Combine(fixture, "Fernwood.wld") };
var worldB = new Terraria.WorldFileData { UniqueId = Guid.NewGuid(), Path = Path.Combine(fixture, "Copper coast.wld") };
File.WriteAllText(worldA.Path, "world-a-first");
File.WriteAllText(worldB.Path, "world-b-first");
var gameThread = new Thread(() => { foreach (Action work in Game.Queue.GetConsumingEnumerable()) work(); }) { IsBackground = true };
gameThread.Start();

Task<T> OnGame<T>(Func<T> work)
{
    var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    Game.QueueMainThreadAction(() => { try { done.SetResult(work()); } catch (Exception e) { done.SetException(e); } });
    return done.Task;
}
void Assert(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}

await OnGame(() =>
{
    TDatabase.Load();
    Game.ActiveWorldFileData = worldA; Game.worldName = "Fernwood";
    WorldOperations.Init(); WorldOperations.Commit(worldA.UniqueId.ToString(), "Found a place to call home");
    Game.ActiveWorldFileData = worldB; Game.worldName = "Copper coast";
    WorldOperations.Init(); WorldOperations.Commit(worldB.UniqueId.ToString(), "A new shore");
    Game.ActiveWorldFileData = worldA; Game.worldName = "Fernwood";
    return true;
});

using var server = new HttpServer(Console.Error.WriteLine);
server.Start();
if (args.Contains("--serve"))
{
    Console.WriteLine("Fixture server: " + server.Url);
    Console.WriteLine("Temporary saves: " + fixture);
    await Task.Delay(Timeout.Infinite);
    return;
}
using var client = new HttpClient { BaseAddress = new Uri(server.Url), Timeout = TimeSpan.FromSeconds(25) };
using (var secondServer = new HttpServer(Console.Error.WriteLine))
{
    secondServer.Start();
    Assert(secondServer.Url != server.Url, "occupied ports fall back to the next local port");
}
string a = worldA.UniqueId.ToString(), b = worldB.UniqueId.ToString();
async Task<JsonNode> State() => JsonNode.Parse(await client.GetStringAsync("api/state"));
JsonNode state = await State();
string token = state["token"].GetValue<string>();
async Task<(HttpStatusCode Status, JsonNode Body)> Post(string kind, string repo = null, string name = null, string branch = null, string hash = null, string message = null, bool authorized = true, string origin = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "api/action")
    {
        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { action = kind, repoId = repo, name, branch, hash, message }), System.Text.Encoding.UTF8, "application/json")
    };
    if (authorized) request.Headers.Add("X-Terragit-Token", token);
    if (origin != null) request.Headers.Add("Origin", origin);
    using var response = await client.SendAsync(request);
    return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync()));
}

Assert(state["repositories"].AsArray().Count == 2, "state lists both repositories");
Assert(state["activeRepositoryId"].GetValue<string>() == a, "state identifies loaded world");
using (var html = await client.GetAsync(""))
{
    Assert(html.IsSuccessStatusCode && (await html.Content.ReadAsStringAsync()).Contains("Save a moment"), "HTML is served from packaged assets");
    Assert(html.Headers.Contains("Content-Security-Policy"), "HTML includes content security policy");
}
Assert((await client.GetStringAsync("app.js")).Contains("renderCommits"), "JavaScript asset is served");
Assert((await client.GetStringAsync("style.css")).Contains("@media"), "responsive CSS asset is served");
Assert((await Post("rename", a, name: "bad", authorized: false)).Status == HttpStatusCode.Forbidden, "writes reject missing session token");
Assert((await Post("rename", a, name: "bad", origin: "https://example.com")).Status == HttpStatusCode.Forbidden, "writes reject cross-origin requests");
using (var forgedHost = new HttpRequestMessage(HttpMethod.Get, "api/state"))
{
    forgedHost.Headers.Host = "evil.invalid";
    Assert(!(await client.SendAsync(forgedHost)).IsSuccessStatusCode, "requests reject foreign Host headers");
}
Assert((await Post("rename", b, name: "Copper coast archive")).Status == HttpStatusCode.OK, "can rename another world's repository");
Assert((await Post("branch", b, name: "harbor", branch: "Main")).Status == HttpStatusCode.OK, "can create a branch in another repository");
await OnGame(() =>
{
    var repo = TDatabase.GetRepository(b);
    repo.Branches.Add(new Branch { Name = "empty" });
    TDatabase.Save();
    TDatabase.Load();
    return true;
});
Assert(await OnGame(() => TDatabase.GetRepository(b).Branches.Single(branch => branch.Name == "empty").LatestCommitHash == null), "empty branches stay empty after reload");
Assert((await Post("branch", b, name: "harbor", branch: "Main")).Status == HttpStatusCode.BadRequest, "duplicate branches rejected");
Assert((await Post("branch", b, name: "two words", branch: "Main")).Status == HttpStatusCode.BadRequest, "branch names remain usable from chat");
Assert((await Post("deleteBranch", b, branch: "Main")).Status == HttpStatusCode.BadRequest, "current branch cannot be deleted");
Assert((await Post("commit", b, message: "wrong world")).Status == HttpStatusCode.BadRequest, "cannot commit the loaded world into a different repository");
Assert((await Post("checkout", b, branch: "Main")).Status == HttpStatusCode.BadRequest, "cannot restore a different repository's world");
Assert((await Post("rename", "missing", name: "missing")).Status == HttpStatusCode.BadRequest, "unknown repositories are rejected");
string hashA = state["repositories"][0]["commits"][0]["commitHash"].GetValue<string>();
string hashB = state["repositories"][1]["commits"][0]["commitHash"].GetValue<string>();
Assert((await client.GetStringAsync("api/snapshot?repo=" + a + "&hash=" + hashA)) == "world-a-first", "snapshot downloads contain original bytes");
Assert((await client.GetAsync("api/snapshot?repo=" + a + "&hash=" + hashB)).StatusCode == HttpStatusCode.BadRequest, "snapshot downloads require repository membership");
Assert((await client.GetAsync("api/snapshot?repo=" + a + "&hash=../../database.json")).StatusCode == HttpStatusCode.BadRequest, "snapshot paths cannot escape blob storage");
Assert((await OnGame(() => File.ReadAllText(TDatabase.GetPushedCommitPath()))) == "world-a-first", "same-named Main branches remain isolated across worlds");
Assert((await Post("message", b, hash: hashB, message: "<script>alert(1)</script>")).Status == HttpStatusCode.OK, "commit messages are accepted as text");
Assert((await Post("diff", a, branch: "Main")).Body["matches"].GetValue<bool>(), "diff matches shared committed data");
File.WriteAllText(worldA.Path, "world-a-second");
Assert(!(await Post("diff", a, branch: "Main")).Body["matches"].GetValue<bool>(), "diff detects saved world changes");
Assert((await Post("commit", a, message: "Built the observatory")).Status == HttpStatusCode.OK, "website commits through shared world/database service");
Assert((await Post("commit", a, message: "Duplicate")).Status == HttpStatusCode.BadRequest, "unchanged commits do not create self-linked heads");
Assert((await Post("branch", a, name: "observatory", branch: "Main")).Status == HttpStatusCode.OK, "branch creation inherits the chosen head");
Assert((await Post("deleteBranch", a, branch: "observatory")).Status == HttpStatusCode.OK, "noncurrent branch deletion succeeds");
await OnGame(() =>
{
    string legacy = Path.Combine(fixture, "Terragit", "remote", Utils.Hash(System.Text.Encoding.UTF8.GetBytes("observatory")) + ".ref");
    File.WriteAllText(legacy, hashA);
    return true;
});
Assert((await Post("branch", a, name: "observatory", branch: "Main")).Status == HttpStatusCode.OK, "deleted branch names can be reused");
Assert(await OnGame(() => File.ReadAllText(TDatabase.GetPushedCommitPath("observatory")) == "world-a-second"), "recreated branch ignores stale legacy references");
await OnGame(() => { TDatabase.Load(); return true; });
state = await State();
Assert(state["repositories"][1]["name"].GetValue<string>() == "Copper coast archive", "repository edits persist after database reload");
Assert(state["repositories"][1]["branches"][1]["latestCommitHash"].GetValue<string>() == hashB, "branch head persists after reload");
Assert(state["repositories"][0]["commits"].AsArray().Count == 2, "web-created commit visible to shared database after reload");
Assert((await Post("restore", a, hash: hashA)).Status == HttpStatusCode.OK, "historical restore can be requested");
await OnGame(() => true); // barrier after the save callback
Assert(File.ReadAllText(worldA.Path) == "world-a-first", "restore replaces the active file with requested snapshot");
Assert(Directory.GetFiles(fixture, "*.terragit-backup-*").Any(path => File.ReadAllText(path) == "world-a-second"), "restore preserves the previous world in a backup");
Assert((await OnGame(() => TDatabase.GetCurrentBranch().LatestCommitHash)) != hashA, "historical restore does not move the branch head");
await OnGame(() => { Game.gameMenu = true; return true; });
Assert((await Post("commit", a, message: "menu")).Status == HttpStatusCode.BadRequest, "world actions reject main-menu state");
Assert((await Post("rename", a, name: "Fernwood archive")).Status == HttpStatusCode.OK, "repository management works without a world loaded");
await OnGame(() => { Game.gameMenu = false; worldA.IsCloudSave = true; return true; });
Assert((await Post("pull", a)).Status == HttpStatusCode.BadRequest, "world actions reject cloud saves");
await OnGame(() => { worldA.IsCloudSave = false; return true; });

// Simulate Terraria being unresponsive while a mutation is queued.
using var release = new ManualResetEventSlim();
var waiting = new TaskCompletionSource();
Game.QueueMainThreadAction(() => { waiting.SetResult(); release.Wait(); });
await waiting.Task;
var timedOut = await Post("rename", a, name: "must never apply");
Assert(timedOut.Status == HttpStatusCode.ServiceUnavailable, "queued request times out while the game is busy");
release.Set();
Assert(await OnGame(() => TDatabase.GetRepository(a).Name == "Fernwood archive"), "timed-out mutation never runs later");
server.Dispose();
using var restarted = new HttpServer(Console.Error.WriteLine);
restarted.Start();
Assert(restarted.Url == server.Url, "unload releases the port for the next load");
Console.WriteLine("All integration checks passed. Temporary fixture: " + fixture);
