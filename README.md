# Terragit

Terragit brings Git-inspired version control to Terraria worlds running on tModLoader.
It stores world snapshots as commits, keeps a history for each world, and lets you
restore snapshots from the in-game chat or a local web interface.

## Features

- One repository per Terraria world.
- Commit world snapshots with a message.
- Commit history tracked by branch.
- Commit and pull commands backed by local save data.
- A simple world diff based on file size and SHA-256 hashes.
- A local website for browsing and managing repositories, commits, and branches.
- Snapshots of the active local world file (normally `.wld`).

Snapshots currently contain one file, not a complete world/mod-data bundle.
The companion `.twld` file is not committed or restored alongside the `.wld`.

Terragit stores its data in the tModLoader save directory:

```text
<tModLoader SavePath>/Terragit/
├── database.json
├── commits/
└── remote/
```

The `remote` directory contains local branch-head references used by `pull` and
`checkout`. Network remotes and external Git hosting are not implemented yet.

## Local website

The website starts automatically when Terragit loads and stops when the mod
unloads. Open **http://127.0.0.1:17840/** in your browser. If that port is occupied,
Terragit tries ports 17841–17849. Run `/tg web` or check the tModLoader log for the
actual address. No Node.js install, separate web process, or internet connection
is required.

The interface includes:

- Search and browse all repositories in this installation's Terragit database.
- View commit messages, timestamps, hashes, and branch heads.
- Rename repositories, edit commit messages, and create branches from an existing
  branch. Renaming a repository does not rename the Terraria world.
- Delete a non-current branch while retaining its commits and snapshot files.
- Download a snapshot as a raw `.bin` file.
- Initialize, commit, compare, pull, check out a branch, or restore an older
  snapshot for the currently loaded local single-player world.

Creating a branch in the website does not switch the active branch. Select the
branch and use **Check out** to switch and restore its snapshot. Restoring an
individual historical commit does not move the branch head; commit afterward
if you want to record the restored state.

“Other repositories” means other worlds already initialized in this local
database. You can edit their metadata and branches without loading those worlds.
Load the corresponding world to commit or restore its files. Cloud saves and
multiplayer world operations are not supported. Repository browsing and metadata
management also work from the main menu.

The website and chat commands both use `Database.cs` and `WorldOperations.cs`.
Browser operations are queued on Terraria's game thread. Keep tModLoader running;
if it is busy or paused in the background, a request can time out. Return to the
game and refresh the page. Timed-out queued mutations are cancelled.

The HTTP server listens only on the loopback address. Writes require a
per-session token, and foreign origins and Host headers are rejected. This is a
local application, not an authenticated multi-user hosting service.

## Installation

### From the Steam Workshop

If a Workshop release is available for your tModLoader version:

1. Subscribe to Terragit on the Steam Workshop.
2. Start tModLoader.
3. Open **Workshop > Manage Mods**.
4. Enable Terragit and reload mods.

### From source

1. Install tModLoader and make sure its mod development tools are available.
2. Copy or clone this project into the tModLoader `ModSources` directory.
3. Open tModLoader and select **Workshop > Develop Mods**.
4. Build Terragit, then enable it from the Mods menu.

The project expects the standard tModLoader layout, with `tModLoader.targets`
available one directory above the project, as in the normal `ModSources` setup.

## Quick start

Load or create a world, then run these commands in chat:

```text
/tg init
/tg commit "initial snapshot"
```

After making more changes:

```text
/tg commit "built the new house"
```

To restore the latest snapshot on the current branch:

```text
/tg pull
```

The world must be saved before committing it because `commit` reads the saved
world file directly. Pulling and checking out save and quit, back up the current
file, replace it with the snapshot, then reload. Backups are written beside the
world as `<world-file>.terragit-backup-<timestamp>`; an existing `.twld` sidecar is
also backed up. The sidecar is not rolled back with the snapshot, so test restores
on a disposable world before relying on them for modded data.

## Commands

| Command | Description |
| --- | --- |
| `/tg init` | Creates a repository for the current world. Repairs older repositories that do not have an initial branch. |
| `/tg commit "message"` | Reads the active saved world file, creates a commit, and updates the current branch's local reference. Rejects unchanged snapshots. |
| `/tg pull` | Restores the current branch's local reference, or its latest commit when no reference exists. |
| `/tg pull <branch>` | Restores the named branch's local reference, or its latest commit when no reference exists. |
| `/tg checkout [branch]` | Switches to an existing branch and restores its local reference or latest commit. Defaults to the current branch. |
| `/tg diff` | Compares the current world file with the latest local commit. |
| `/tg diff <branch>` | Compares the current world file with a named branch's latest commit. |
| `/tg web` | Shows the local website's address. |

`/tg init` creates the `Main` branch. Create additional branches from the website.
Command and branch names are case-sensitive. Pulling a named branch does not
switch the current branch.

## Development notes

Terragit is an early project. Commits are complete world-file snapshots rather
than tile-level diffs. All repository data is stored locally, and commit/pull
currently do not communicate with a server or external Git host. Network
synchronization can be added later without changing the basic in-game workflow.

## License

See [LICENSE](LICENSE).
