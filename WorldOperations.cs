using System;
using System.IO;
using Terraria;
using Terraria.ID;

namespace Terragit;

// Shared by chat and HTTP. All entry points run on Terraria's main thread.
public static class WorldOperations
{
    public static bool RestoreInProgress { get; private set; }
    public static string RestoreStatus { get; private set; }
    private static int generation;

    public static bool HasLocalWorld => !Main.gameMenu && !Main.dedServ
        && Main.netMode == NetmodeID.SinglePlayer && Main.ActiveWorldFileData != null
        && !Main.ActiveWorldFileData.IsCloudSave;

    public static string ActiveRepositoryId => HasLocalWorld ? Main.ActiveWorldFileData.UniqueId.ToString() : null;

    public static void Reset()
    {
        generation++;
        RestoreInProgress = false;
        RestoreStatus = null;
    }

    public static void RequireWorld(string repositoryId = null)
    {
        if (RestoreInProgress) throw new InvalidOperationException("A world restore is already in progress.");
        if (!HasLocalWorld) throw new InvalidOperationException("Load a local single-player world first. Cloud saves and multiplayer world actions are not supported.");
        if (repositoryId != null && repositoryId != ActiveRepositoryId)
            throw new InvalidOperationException("Load this repository's world before changing its world data.");
    }

    public static string GetWorldFilePath()
    {
        RequireWorld();
        string path = Main.ActiveWorldFileData.Path;
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
        throw new InvalidOperationException("The saved world file was not found. Save the world first.");
    }

    public static void Init()
    {
        RequireWorld();
        TDatabase.Init();
    }

    public static void Commit(string repositoryId, string message)
    {
        RequireWorld(repositoryId);
        if (TDatabase.GetCurrentRepo() == null) throw new InvalidOperationException("Initialize the repository first.");
        if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
            throw new ArgumentException("Enter a commit message of 1–500 characters.");
        TDatabase.currentWorldData = File.ReadAllBytes(GetWorldFilePath());
        try
        {
            TDatabase.CreateCommit(message.Trim());
            if (TDatabase.PushCurrentBranch() == null)
                throw new InvalidOperationException("Commit saved, but updating the local reference failed.");
        }
        finally { TDatabase.currentWorldData = null; }
    }

    public static WorldDiff Diff(string repositoryId, string branch)
    {
        RequireWorld(repositoryId);
        string snapshot = TDatabase.GetLatestCommitPath(branch);
        if (snapshot == null || !File.Exists(snapshot)) throw new InvalidOperationException("This branch has no available snapshot.");
        byte[] current = File.ReadAllBytes(GetWorldFilePath());
        byte[] saved = File.ReadAllBytes(snapshot);
        string currentHash = Utils.Hash(current), commitHash = Utils.Hash(saved);
        return new WorldDiff(currentHash == commitHash, current.Length, saved.Length, currentHash, commitHash);
    }

    public static void Restore(string repositoryId, string branch, bool checkout, string hash = null)
    {
        RequireWorld(repositoryId);
        Repository repo = TDatabase.GetCurrentRepo() ?? throw new InvalidOperationException("Initialize the repository first.");
        branch ??= repo.CurrentBranch;
        if (TDatabase.GetBranch(branch) == null) throw new ArgumentException("Branch not found.");
        string snapshot = hash != null ? TDatabase.GetSnapshotPath(repo.Guid, hash)
            : TDatabase.GetPushedCommitPath(branch) ?? TDatabase.GetLatestCommitPath(branch);
        if (snapshot == null || !File.Exists(snapshot)) throw new InvalidOperationException("This branch has no available snapshot.");
        string worldPath = GetWorldFilePath();
        string backupPath = worldPath + ".terragit-backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        int operationGeneration = generation;
        RestoreInProgress = true;
        RestoreStatus = "Saving the world before restoring…";
        try
        {
            WorldGen.SaveAndQuit(() =>
            {
                // The save callback can run on a worker. Rejoin the game thread
                // before touching database state or loading a world.
                Main.QueueMainThreadAction(() =>
                {
                    if (operationGeneration != generation) return;
                    try
                    {
                        File.Copy(worldPath, backupPath, false);
                        string sidecar = Path.ChangeExtension(worldPath, ".twld");
                        if (File.Exists(sidecar)) File.Copy(sidecar, backupPath + ".twld", false);
                        File.Copy(snapshot, worldPath, true);
                        if (checkout) TDatabase.CheckoutBranch(branch);
                        RestoreStatus = "Snapshot restored. Backup: " + Path.GetFileName(backupPath);
                        WorldGen.playWorld();
                    }
                    catch (Exception ex) { RestoreStatus = "Restore failed: " + ex.Message; }
                    finally { RestoreInProgress = false; }
                });
            });
        }
        catch
        {
            RestoreInProgress = false;
            RestoreStatus = "Restore could not start.";
            throw;
        }
    }
}

public record WorldDiff(bool Matches, long CurrentSize, long CommitSize, string CurrentHash, string CommitHash);
