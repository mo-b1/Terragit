using System;
using System.IO;
using Terraria;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;
using System.Text.Json;
using System.Security.Cryptography;

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
    }

    public static void Save()
    {
        // indented for readability
        var options = new JsonSerializerOptions {WriteIndented = true};
        string jsonString = JsonSerializer.Serialize(Data, options);
        File.WriteAllText(JSONpath, jsonString);
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
        EnsureCurrentBranch(GetCurrentRepo());
        Save();

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
        
        foreach (Repository repo in Data.Repositories)
        {
            if (repo.Guid == Main.ActiveWorldFileData.UniqueId.ToString())
            {
                // Return the repo once found
                return repo;
            }
        }
        
        // Not found, need to create repo first
        return null;
    }

    private static void EnsureCurrentBranch(Repository repo)
    {
        if (repo == null) return;

        if (repo.Branches == null)
            repo.Branches = new System.Collections.Generic.List<Branch>();
        if (repo.Commits == null)
            repo.Commits = new System.Collections.Generic.List<Commit>();

        if (string.IsNullOrEmpty(repo.CurrentBranch))
            repo.CurrentBranch = DefaultBranch;

        foreach (Branch branch in repo.Branches)
        {
            if (branch.Name == repo.CurrentBranch)
                return;
        }

        repo.Branches.Add(new Branch { Name = repo.CurrentBranch });
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

        if (repo.Branches == null) return null; 

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
        // Get the current repository
        Repository repo = GetCurrentRepo();

        // If not found then repo hasn't been created, /tg init must be ran
        if (repo == null)
        {
            Main.NewText("You must run '/tg init' first to create the repo!", Microsoft.Xna.Framework.Color.Red);
            return;
        }
        
        Branch newBranch = new Branch();
        newBranch.Name = branchName;

        // Check no existing branch has the same name
        foreach (Branch b in repo.Branches)
        {
            if (b.Name == branchName)
            {
                Main.NewText("A branch already exists with that name. ", Microsoft.Xna.Framework.Color.Red);
                return;
            }
        }

        // Make it the current branch
        // Add it to branches
        repo.CurrentBranch = branchName;
        repo.Branches.Add(newBranch);
        Save();

    }
    
    public static Branch GetCurrentBranch()
    {
        Repository repo = GetCurrentRepo();
        if (repo == null) return null;
        return GetBranch(repo.CurrentBranch);
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

        string branchKey = Utils.Hash(System.Text.Encoding.UTF8.GetBytes(branch.Name));
        string remoteRef = Path.Combine(RemoteFolder, branchKey + ".ref");
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

        string branchKey = Utils.Hash(System.Text.Encoding.UTF8.GetBytes(branch.Name));
        string remoteRef = Path.Combine(RemoteFolder, branchKey + ".ref");
        if (!File.Exists(remoteRef)) return null;

        string commitHash = File.ReadAllText(remoteRef).Trim();
        if (string.IsNullOrEmpty(commitHash)) return null;

        string commitPath = Path.Combine(CommitsFolder, commitHash + ".bin");
        return File.Exists(commitPath) ? commitPath : null;
    }

    public static Commit GetCommit(string commitHash)
    {
        Repository repo = GetCurrentRepo();

        // if no commits
        if (repo.Commits == null) return null;

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

}
