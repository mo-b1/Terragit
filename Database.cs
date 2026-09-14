using System;
using System.IO;
using Terraria;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;
using System.Text.Json;

namespace Terragit;

public static class Database
{
    // Paths to the save path 
    // Json db file and commits stored in save path
    public static string TerragitFolder => Path.Combine(Main.SavePath, "Terragit");
    public static string JSONpath => Path.Combine(TerragitFolder,  "database.json");
    public static string CommitsFolder => Path.Combine(TerragitFolder, "commits");

    // Holds db of all repos in memory
    public static TerragitData Data = new TerragitData();

    // Branch set to this upon /tg init
    public static string DefaultBranch = "Main";

    public static void Load()
    {
        if (File.Exists(JSONpath))
        {
            string jsonString = File.ReadAllText(JSONpath);
            Data = JsonSerializer.Deserialize<TerragitData>(jsonString) ?? new TerragitData();
        }
    }

    public static void Save()
    {
        // indented for readability
        var options = new JsonSerializerOptions {WriteIndented = true};
        string jsonString = JsonSerializer.Serialize(Data, options);
        File.WriteAllText(JSONpath, jsonString);
    }

    
    // Creates a repo with the name being the world name
    public static bool CreateRepo()
    {
        // Replace any spaces from world name with _
        var worldName = Main.worldName;
        worldName = worldName.Replace(" ", "_");

        // Create the instance of the repo
        Repository newRepo = new Repository();
        newRepo.Name = worldName;
        newRepo.Guid = Main.ActiveWorldFileData.UniqueId.ToString();
        newRepo.CurrentBranch = DefaultBranch;
        
        // Check if repo already exists
        foreach (Repository repo in Data.Repositories)
        {
            if (repo.Guid == newRepo.Guid)
            {
                return false;
            }
        }
        
        // Add the repo to the list of repositories
        Data.Repositories.Add(newRepo);
        return true;
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
    }

    public static Branch GetBranch(Repository repo, string name) 
    {
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

    }

    // Gets the path to the latest commit on the current branch
    public static string GetLatestCommitPath()
    {
        // Get the current repository
        Repository repo = GetCurrentRepo();

        // If not found then repo hasn't been created, /tg init must be ran
        if (repo == null)
        {
            Main.NewText("You must run '/tg init' first to create the repo!", Microsoft.Xna.Framework.Color.Red);
            return null;
        }

        if (repo.CurrentBranch == null) 
        {
            Main.NewText("No current branch for this world: You must make a commit", Microsoft.Xna.Framework.Color.Red);
        }
        
        Branch CurrentBranch = GetBranch(repo, repo.CurrentBranch);

        if (CurrentBranch == null) {
            Main.NewText("Current branch not found", Microsoft.Xna.Framework.Color.Red);
            return null;
        }

        Commit LatestCommit = GetCommit(repo, CurrentBranch.LatestCommitHash);

        if ( LatestCommit == null ) {
            Main.NewText("Current branch found but commit not found", Microsoft.Xna.Framework.Color.Red);
            return null;
        }

        return LatestCommit.PathToWorldFile;
    }

    public static Commit GetCommit(Repository repo, string commitHash)
    {
        foreach (Commit commit in repo.Commits) 
        {
            if (commit.CommitHash ==  commitHash) {
                return commit;
            }
        }
        return null;   
    }

    // Creates a commit on the current branch
    public static void CreateCommit(string commitHash, string message, string previousCommitHash)
    { 
        // Create the new instance of the commit
        var commit = new Commit();
        commit.Message = message;
        commit.CommitHash = commitHash;
        commit.CommitTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // Get the current repository
        Repository repo = GetCurrentRepo();

        // If not found then repo hasn't been created, /tg init must be ran
        if (repo == null)
        {
            Main.NewText("You must run '/tg init' first to create the repo!", Microsoft.Xna.Framework.Color.Red);
            return;
        }

        // Alter the current branch to point to that commit
        foreach (Branch b in repo.Branches)
        {
            if (b.Name == repo.CurrentBranch)
            {
                // Get the previous commit hash
                commit.PreviousCommitHash = b.LatestCommitHash;
                // Update it 
                b.LatestCommitHash = commitHash;
            }
        }
        
        // Add the commit to the repo after getting the previous commit hash
        repo.Commits.Add(commit);
    }


}