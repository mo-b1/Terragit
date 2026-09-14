using System.Collections.Generic;
using Terraria;

namespace Terragit;

public class TerragitData
{
    public List<Repository> Repositories { get; set; } = new List<Repository>();
}

public class Commit
{
    public string CommitHash { get; set; }
    public string CommitTimestamp{ get; set; }
    public string Message{ get; set; }
    public string PathToWorldFile{ get; set; }
    public string PreviousCommitHash{ get; set; }
}

public class Repository
{
    public string Name{ get; set; }
    public string Guid { get; set; }
    public string CurrentBranch{ get; set; }
    public List<Branch> Branches {get; set; }
    public List<Commit> Commits {get; set;}

}

public class Branch
{
    public string Name{ get; set; }
    public string LatestCommitHash{ get; set; }
}

