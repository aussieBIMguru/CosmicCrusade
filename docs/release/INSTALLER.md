# Building the Cosmic Crusade installer

The result is one file, `artifacts\CosmicCrusade-Setup-<version>.exe`. Your friends run it and get Cosmic Crusade in their Start menu (and on the desktop). It's built the same way as Throng's installer: Inno Setup plus a publish script.

## What the installer does
- Installs **per user, with no admin prompt**, into `%LocalAppData%\Programs\Cosmic Crusade\`. That folder holds the app and `data\` (cards, art, strips, layout, pools).
- It's **self-contained**: players don't need .NET installed.
- Shows the **licence** as an information page: MIT for the software, the game content all rights reserved. Nobody has to tick "I accept".
- Adds **Start menu** shortcuts:
  - **Cosmic Crusade**;
  - **Cosmic Crusade How to Play**: the player guides, installed in `docs\`;
  - **Cosmic Crusade LAN Server**: for playing on a home network.

  It also adds a **desktop** shortcut, which can be unticked, and offers "Play Cosmic Crusade now" at the end.
- Includes the **LAN Server** (`CosmicCrusade-LanServer.exe`), a single self-contained file. See `docs/players/LAN-GUIDE.md`.
- If `data\online.json` has a `defaultServer`, every copy you hand out comes with your online server pre-filled.
- **Upgrades in place.** A newer setup finds the old install through its fixed AppId and replaces the app folder cleanly. Players' settings (name, server address in `%AppData%\CosmicCrusade`) are kept, including through an uninstall.
- If the game is running, it asks the player to close it first. It never closes the game for them, since they could be mid-game online.
- The game needs no firewall rule: online play only connects *out* to the relay. The LAN Server listens on port 5080, so Windows asks to allow it the first time it's started.
- Appears in **Settings → Apps** as "Cosmic Crusade 1.0.0", with an Uninstall button.

## One-time setup (your PC)
1. **Install Inno Setup 6.** Download it from <https://jrsoftware.org/isdl.php> ("innosetup-6.x.x.exe") and run it with the default options. This is a build tool on your PC; it isn't shipped to players.
2. You already have the **.NET 8 SDK** (it comes with Visual Studio).

## Building a release
1. **Put the card art in `data\art`.** The script stops if `data\art` has no images, because you leave the art out of the zips. Passing `-AllowMissingArt` builds anyway, for trying the installer out.
2. **Set the version** in `Directory.Build.props`, e.g. `<Version>1.0.1</Version>`. It's used everywhere:
   - the exe,
   - the window title ("Cosmic Crusade 1.0.1"),
   - the installer,
   - the setup file name.

   Bump it every time you send out a new build, so you and your friends can see you're on the same one. That matters for online play.
3. **Close the game** if it's running, then double-click **`publish.bat`** in the solution folder.
   - It runs the tests and publishes the app to `artifacts\app\`.
   - It checks the exe, licence, guides, card data and every art image made it into the publish.
   - It adds the LAN Server, reports the default server, and compiles the installer.
   - It takes a minute or two (the tests are most of it). Add `-SkipTests` to skip them for a quick try, e.g. `publish.bat -SkipTests` from a command prompt.
4. When it says **"Done"**, the `artifacts` folder holds:
   - `CosmicCrusade-Setup-1.0.0.exe`: send this to your friends;
   - `CosmicCrusade-Setup-1.0.0.sha256.txt`: its checksum, in case anyone wants to check the download;
   - `app\`: the published app the installer was made from.
5. **Try it yourself:** run the setup, start the game from the Start menu, and check the cards have their art.

## The Visual Studio way (optional)
- In Solution Explorer, right-click **CosmicCrusade.App** → **Publish…**, pick the **Release-win-x64** profile, then click **Publish**.
  - This writes the same self-contained app to `artifacts\app\`.
  - It doesn't run the tests, check the art or add the LAN Server, so that setup won't include it.
- Then open `installer\CosmicCrusade.iss` in the **Inno Setup Compiler** (double-click the file) and press **Compile** (Ctrl+F9). The version is read from the published exe. The setup appears in `installer\Output\`.

`publish.bat` is the better route for real releases.

## Sending it to friends
- Send the setup exe however you like: a OneDrive or Google Drive link, Discord, email (it's roughly 50–70 MB, so a link is easiest).
- **Windows SmartScreen** will say "Windows protected your PC", because the setup isn't code-signed. Tell friends to click **More info → Run anyway**. Code signing removes this; it needs a paid certificate. `publish.bat -Sign -CertThumbprint <thumbprint>` is ready for it.
- Online play needs everyone on the **same version and card data**. The game checks this when you join and says so if not.
- Also send them the **server address** (your `…workers.dev` relay) for the Server box. Or bake it into the build: `defaultServer` in `data\online.json` (see `docs/players/HOST-YOUR-OWN-SERVER.md`, step 8).

## Troubleshooting
| Problem | Fix |
|---|---|
| "Inno Setup 6 (ISCC.exe) was not found" | Install Inno Setup 6, or pass its path: `publish.bat -InnoCompiler "D:\Tools\Inno Setup 6\ISCC.exe"`. |
| "data\art has no card images" | Copy your art into `data\art`, or add `-AllowMissingArt` for a test build. |
| "The tests failed" | Fix them (or `-SkipTests` for a quick test build only). The failing test is listed above the message. |
| "running scripts is disabled on this system" | Use `publish.bat` rather than running the .ps1 directly; it passes `-ExecutionPolicy Bypass` for that run only. |
| The setup says to close Cosmic Crusade | Close every game window (check the taskbar) and choose Retry. |
| Inno's compiler says `GetVersionComponents` is unknown | Your Inno Setup is older than 6.1: update it. `publish.bat` doesn't need it, because it passes the version itself. |

## Files
| File | What it is |
|---|---|
| `Directory.Build.props` | `<Version>`, product, author, copyright for every project. |
| `publish.bat` → `build\publish.ps1` | The release build: version, tests, publish, checks, installer, checksum. |
| `installer\CosmicCrusade.iss` | The Inno Setup script. Its **AppId must never change**: that's how upgrades find the old install. |
| `src\CosmicCrusade.App\Properties\PublishProfiles\Release-win-x64.pubxml` | The Visual Studio publish profile (same settings as the script). |
| `LICENSE`, `THIRD-PARTY-NOTICES.md` | Shipped with the app; `LICENSE` is also the installer's information page. |
| `docs\players\*.md` | The player guides, shipped in `docs\` next to the exe and linked from the main menu. |
| `data\online.json` | `defaultServer`: the online server this build points at (empty = players type one). |
| `clean.bat` | Deletes bin/obj/TestResults, `artifacts\` and the relay's local cache before zipping the solution. |
