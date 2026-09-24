using System;
using System.IO;
using Terraria;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;
using System.Text.Json;
using System.Security.Cryptography;
using System.Linq;
using System.Collections.Generic;

namespace Terragit;

public static class TDatabase
{
    // Paths to the save path 
    // Json db file and commits stored in save path
    private static string TerragitFolder => Path.Combine(Main.SavePath, "Terragit");
    private static string JSONpath => Path.Combine(TerragitFolder,  "database.json");
    private static string CommitsFolder => Path.Combine(TerragitFolder, "commits");
    private static string RemoteFolder => Path.Combine(TerragitFolder, "remote");
    private static byte[] _currentWorldData;
    public static byte[] currentWorldData
    {
        get => _currentWorldData;
        set {
            _currentWorldData = value;
        }
    }

    // Holds db of all repos in memory
    private static TerragitData Data;

    static TDatabase()
    {
        // Checks if nested directories exist, creates them if not
        Directory.CreateDirectory(TerragitFolder);
        Directory.CreateDirectory(CommitsFolder);
        Directory.CreateDirectory(RemoteFolder);
    }

    // Branch set to this upon /tg init
    public static string DefaultBranch = "Main";

    public static void Load()
    {
        Main.NewText("[Terragit] Loading database...");

        // check if json database exists, if so read the file 
        if (File.Exists(JSONpath))
        {
            string jsonString = File.ReadAllText(JSONpath);
            Data = JsonSerializer.Deserialize<TerragitData>(jsonString) ?? new TerragitData();
        } else
        {
            Data = new TerragitData();
        }

        if (Data.Repositories == null)
            Data.Repositories = new System.Collections.Generic.List<Repository>();

        bool repaired = false;
        foreach (Repository repo in Data.Repositories)
            repaired |= EnsureCurrentBranch(repo);

        if (repaired)
            Save();
    }

    public static void Save()
    {
        // indented for readability
        var options = new JsonSerializerOptions {WriteIndented = true};
        string jsonString = JsonSerializer.Serialize(Data, options);
        string temporaryPath = JSONpath + ".tmp";
        File.WriteAllText(temporaryPath, jsonString);
        File.Move(temporaryPath, JSONpath, true);
    }

    
    public static Repository Init()
    {
        Load();
        if (GetCurrentRepo() == null)
        {
            Repository r = CreateRepo();
            if (r != null){
                return r;
            }             
        }

        // Older database files were created without an initial branch. Repair
        // those repositories when they are opened.
        Repository existingRepo = GetCurrentRepo();
        if (existingRepo != null)
        {
            EnsureCurrentBranch(existingRepo);
            Save();
        }

        return null;
    }
    // Creates a repo with the name being the world name
    public static Repository CreateRepo()
    {
        // Replace any spaces from world name with _
        var worldName = Main.worldName;
        worldName = worldName.Replace(" ", "_");

        // Create the instance of the repo
        Repository newRepo = new Repository();
        newRepo.Name = worldName;
        newRepo.Guid = Main.ActiveWorldFileData.UniqueId.ToString();
        newRepo.CurrentBranch = DefaultBranch;
        newRepo.Branches.Add(new Branch { Name = DefaultBranch });
        
        // Check if repo already exists
        foreach (Repository repo in Data.Repositories)
        {
            if (repo.Guid == newRepo.Guid)
            {
                return null;
            }
        }
        
        // Add the repo to the list of repositories
        Data.Repositories.Add(newRepo);
        Save();
        return newRepo;
    }

    public static Repository GetCurrentRepo()
    {
        // If world hasn't been loaded
        if (Main.ActiveWorldFileData == null) return null;
        
        if (Data?.Repositories == null)
            return null;

        foreach (Repository repo in Data.Repositories)
        {
            if (repo.Guid == Main.ActiveWorldFileData.UniqueId.ToString())
            {
                EnsureCurrentBranch(repo);
                // Return the repo once found
                return repo;
            }
        }
        
        // Not found, need to create repo first
        return null;
    }

    private static bool EnsureCurrentBranch(Repository repo)
    {
        if (repo == null) return false;

        bool changed = false;

        if (repo.Branches == null)
        {
            repo.Branches = new System.Collections.Generic.List<Branch>();
            changed = true;
        }
        if (repo.Commits == null)
        {
            repo.Commits = new System.Collections.Generic.List<Commit>();
            changed = true;
        }

        if (string.IsNullOrEmpty(repo.CurrentBranch))
        {
            repo.CurrentBranch = DefaultBranch;
            changed = true;
        }

        Branch currentBranch = null;
        foreach (Branch branch in repo.Branches)
        {
            if (branch.Name == repo.CurrentBranch)
            {
                currentBranch = branch;
                break;
            }
        }

        if (currentBranch == null)
        {
            currentBranch = new Branch { Name = repo.CurrentBranch };
            repo.Branches.Add(currentBranch);
            changed = true;
        }

        // Older versions stored commits without attaching them to a branch.
        // The commit list is ordered by creation time, so use its last entry
        // as the branch head when migrating that format.
        if (changed && string.IsNullOrEmpty(currentBranch.LatestCommitHash) && repo.Commits.Count > 0)
        {
            Commit latestCommit = repo.Commits[repo.Commits.Count - 1];
            if (latestCommit != null && !string.IsNullOrEmpty(latestCommit.CommitHash))
            {
                currentBranch.LatestCommitHash = latestCommit.CommitHash;
                changed = true;
            }
        }

        return changed;
    }

    // Changes the current branch to another
    public static void ChangeBranch(string branchName)
    {
        // Get the current repository
        Repository repo = GetCurrentRepo();

        // If not found then repo hasn't been created, /tg init must be ran
        if (repo == null)
        {
            Main.NewText("You must run '/tg init' first to create the repo!", Microsoft.Xna.Framework.Color.Red);
            return;
        }

        foreach (Branch b in repo.Branches)
        {
            if (b.Name == branchName)
            {
                repo.CurrentBranch = b.Name;
                Main.NewText("Branch: " + b.Name);
            }
        }
        Save();
    }

    public static Branch GetBranch(string name)  
    {
        Repository repo = GetCurrentRepo();

        if (repo == null || repo.Branches == null) return null;

        foreach (Branch b in repo.Branches){
            if (b.Name == name) {
                return b;
            }
        }       
        return null;
    }

    // Creates a new branch
    public static void CreateBranch(string branchName)
    {
        Repository repo = GetCurrentRepo();
        if (repo == null)
        {
            Main.NewText("You must run '/tg init' first to create the repo!", Microsoft.Xna.Framework.Color.Red);
            return;
        }
        
        CreateBranch(repo.Guid, branchName, repo.CurrentBranch);
        repo.CurrentBranch = branchName;
        Save();

    }
    
    public static Branch GetCurrentBranch()
    {
        Repository repo = GetCurrentRepo();
        if (repo == null) return null;
        return GetBranch(repo.CurrentBranch);
    }

    public static bool CheckoutBranch(string branchName)
    {
        Repository repo = GetCurrentRepo();
        if (repo == null) return false;

        EnsureCurrentBranch(repo);
        foreach (Branch branch in repo.Branches)
        {
            if (branch.Name == branchName)
            {
                repo.CurrentBranch = branch.Name;
                Save();
                return true;
            }
        }

        return false;
    }

    // Gets the path to the latest commit on the current branch
    public static string GetLatestCommitPath(string branchName = null)
    {
        Repository repo = GetCurrentRepo();

        if (repo == null)
        {
            Main.NewText("You must run '/tg init' first to create the repo!", Microsoft.Xna.Framework.Color.Red);
            return null;
        }

        if (branchName == null)
        {
            branchName = repo.CurrentBranch;
        }

        Branch currentBranch = GetBranch(branchName);
        if (currentBranch == null) 
        {
            Main.NewText("No current branch for this world: You must make a commit", Microsoft.Xna.Framework.Color.Red);
            return null;
        }

        if (string.IsNullOrEmpty(currentBranch.LatestCommitHash))
        {
            Main.NewText("No commits on this branch yet", Microsoft.Xna.Framework.Color.Red);
            return null;
        }

        Commit latestCommit = GetCommit(currentBranch.LatestCommitHash);

        if (latestCommit == null) {
            Main.NewText("Current branch found but commit not found", Microsoft.Xna.Framework.Color.Red);
            return null;
        }

        return Path.Combine(CommitsFolder, latestCommit.CommitHash + ".bin");
    }

    public static string PushCurrentBranch()
    {
        string commitPath = GetLatestCommitPath();
        if (commitPath == null || !File.Exists(commitPath)) return null;

        Branch branch = GetCurrentBranch();
        if (branch == null) return null;

        string remoteRef = GetReferencePath(GetCurrentRepo(), branch.Name);
        File.WriteAllText(remoteRef, branch.LatestCommitHash);
        return commitPath;
    }

    public static string GetPushedCommitPath(string branchName = null)
    {
        Repository repo = GetCurrentRepo();
        if (repo == null) return null;

        branchName ??= repo.CurrentBranch;
        Branch branch = GetBranch(branchName);
        if (branch == null) return null;

        string remoteRef = GetReferencePath(repo, branch.Name);
        if (!File.Exists(remoteRef))
        {
            // Read legacy references only if they belong to this repository.
            string branchKey = Utils.Hash(System.Text.Encoding.UTF8.GetBytes(branch.Name));
            remoteRef = Path.Combine(RemoteFolder, branchKey + ".ref");
            if (!File.Exists(remoteRef)) return null;
        }

        string commitHash = File.ReadAllText(remoteRef).Trim();
        if (!IsCommitHash(commitHash) || !repo.Commits.Any(c => c.CommitHash == commitHash)) return null;

        string commitPath = Path.Combine(CommitsFolder, commitHash + ".bin");
        return File.Exists(commitPath) ? commitPath : null;
    }

    public static Commit GetCommit(string commitHash)
    {
        Repository repo = GetCurrentRepo();

        // if no commits
        if (repo?.Commits == null) return null;

        // finds the commit
        foreach (Commit commit in repo.Commits) 
        {
            if (commit.CommitHash ==  commitHash) {
                return commit;
            }
        }
        return null;   
    }

    // Creates a commit on the current branch
    public static void CreateCommit(string message)
    { 
        string commitHash = Utils.Hash(currentWorldData);
        string commitPath = Path.Combine(CommitsFolder, commitHash + ".bin");

        // Create the new instance of the commit
        var commit = new Commit();
        commit.Message = message;
        commit.CommitHash = commitHash;
        commit.CommitTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        commit.PathToWorldFile = commitPath;

        // Get the current repository
        Repository repo = GetCurrentRepo();

        // If not found then repo hasn't been created, /tg init must be ran
        if (repo == null)
        {
            Main.NewText("You must run '/tg init' first to create the repo!", Microsoft.Xna.Framework.Color.Red);
            return;
        }

        EnsureCurrentBranch(repo);

        // Identical content uses the same blob; do not create a self-linked head.
        if (GetCurrentBranch()?.LatestCommitHash == commitHash)
            throw new InvalidOperationException("The saved world already matches the latest commit.");

        // Alter the current branch to point to that commit
        foreach (Branch b in repo.Branches)
        {
            if (b.Name == repo.CurrentBranch)
            {
                // Get the previous commit hash and link the current commit to it
                commit.PreviousCommitHash = b.LatestCommitHash;
                // Update the latest commit
                b.LatestCommitHash = commitHash;
            }
        }

        // Save the world as bytes to disk
        File.WriteAllBytes(commitPath, currentWorldData);
        _currentWorldData = null;
        
        // Add the commit to the repo after getting the previous commit hash
        repo.Commits.Add(commit);
        Save();
    }

    private static string GetReferencePath(Repository repo, string branchName)
    {
        string key = Utils.Hash(System.Text.Encoding.UTF8.GetBytes(repo.Guid + ":" + branchName));
        return Path.Combine(RemoteFolder, key + ".ref");
    }

    private static bool IsCommitHash(string hash) => hash != null && hash.Length == 64 && hash.All(Uri.IsHexDigit);

    // Call these methods on the game thread, just like the chat commands.
    // The web server queues all database work there before serializing a response.
    public static IReadOnlyList<Repository> GetRepositories() => Data.Repositories.AsReadOnly();

    public static Repository GetRepository(string repositoryId) =>
        Data.Repositories.FirstOrDefault(r => r.Guid == repositoryId)
        ?? throw new ArgumentException("Repository not found.");

    private static string ValidateText(string value, string label, int maxLength)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw new ArgumentException($"{label} must contain 1–{maxLength} characters without control characters.");
        return value;
    }

    public static void RenameRepository(string repositoryId, string name)
    {
        GetRepository(repositoryId).Name = ValidateText(name, "Repository name", 120);
        Save();
    }

    public static Branch CreateBranch(string repositoryId, string name, string sourceBranch)
    {
        Repository repo = GetRepository(repositoryId);
        name = ValidateText(name, "Branch name", 80);
        if (name.Any(char.IsWhiteSpace))
            throw new ArgumentException("Branch names cannot contain spaces.");
        if (repo.Branches.Any(b => b.Name == name))
            throw new ArgumentException("A branch with that name already exists.");
        Branch source = repo.Branches.FirstOrDefault(b => b.Name == sourceBranch)
            ?? throw new ArgumentException("Source branch not found.");
        Branch branch = new Branch { Name = name, LatestCommitHash = source.LatestCommitHash };
        repo.Branches.Add(branch);
        // Also write an empty reference for an empty branch so a stale legacy
        // reference with the same name cannot attach unrelated history.
        File.WriteAllText(GetReferencePath(repo, name), branch.LatestCommitHash ?? "");
        Save();
        return branch;
    }

    public static void DeleteBranch(string repositoryId, string name)
    {
        Repository repo = GetRepository(repositoryId);
        if (name == repo.CurrentBranch)
            throw new ArgumentException("The current branch cannot be deleted. Check out another branch first.");
        Branch branch = repo.Branches.FirstOrDefault(b => b.Name == name)
            ?? throw new ArgumentException("Branch not found.");
        repo.Branches.Remove(branch);
        Save();
        File.Delete(GetReferencePath(repo, name));
    }

    public static void UpdateCommitMessage(string repositoryId, string hash, string message)
    {
        Repository repo = GetRepository(repositoryId);
        message = ValidateText(message, "Commit message", 500);
        var commits = repo.Commits.Where(c => c.CommitHash == hash).ToList();
        if (commits.Count == 0) throw new ArgumentException("Commit not found.");
        foreach (Commit commit in commits) commit.Message = message;
        Save();
    }

    public static string GetSnapshotPath(string repositoryId, string hash)
    {
        Repository repo = GetRepository(repositoryId);
        if (!IsCommitHash(hash) || !repo.Commits.Any(c => c.CommitHash == hash))
            throw new ArgumentException("Commit not found in this repository.");
        string path = Path.Combine(CommitsFolder, hash + ".bin");
        if (!File.Exists(path)) throw new InvalidOperationException("The snapshot file is missing.");
        return path;
    }

}
