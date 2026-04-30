# STILL VERY MUCH EARLY IN DEVELOPMENT/PLEASE SEE BELOW
This project is actively being worked on and is ***NOT*** in any state to be used or attempted in an Archipelago game in any form. Ideally, this will be removed within the near future, but for now this shall remain until further notice.

![Intro](Images&Icons/laikabanner.png)
# Laika:Aged Through Blood Archipelago Randomizer
A *Laika: Aged Through Blood* mod for the [Archipelago multi-game randomizer system](https://archipelago.gg).

# Status (as of May 2026)
We have made significant progress! There are certainly plenty of adjustments and tweaks needed to make this stable.

Anyone is free to try it out if they'd like! However, please expect potentially broken or unstable portions. 

# Contact
For questions, feedback, or discussion related to the randomizer, please visit the "`Laika: Aged Through Blood`" thread in the [Archipelago Discord server](https://discord.com/channels/731205301247803413/1491947867345129522), or message me (`@itsseras`) directly on Discord.

# What is an "Archipelago Randomizer", and why would I want one?
![apicon](Images&Icons/archipelago.png)

Archipelago allows for various games to be randomized in a vast amount of ways. Not only that, but Archipelago allows these games to link to each other and send various in game items to one another.

For example, say you're playing *Laika: Aged Through Blood* and buy a map upgrade from Renato. That map upgrade could instead give a legendary item to a *Risk of Rain 2* player in their game. In the same vein, the Risk of Rain 2 player could open a chest and send a Sniper Rifle to you in your game. This allows for unique and dynamic gameplay styles and a wide variety of approaching games that were not possible prior. 

# What This Specific Mod Changes
Laika came prepared when departing to Where All Was Lost. She is now armed with her trusty pistol at the start of the game, alongside her iconic reflect bike skill. Unfortunately, the birds had been tipped off prior to Laika's arrival, so they'll appear far quicker than what's expected.

Not that Laika minds. After all, the last thing she'd want is for anything to happen to Jakob.

Flavor aside, (nearly) everything from Map purchases from Renato, cassette tapes, bike upgrades, and even key items are randomized.

Depending on your `yaml` options, you can either receive weapons outright, or you can receive materials to craft the weapon instead.

The end goal is still to put an end to the final boss before they [SPOILER]. In the future, alternate win conditions are planned. (Example: Mother of the Year Award, Getting the Band Back Together, Master Chef, etc etc).

# Installation
### Prerequisites
- Make sure you have Laika: Aged Through Blood installed.
- Download the [latest version of BepInEx](https://github.com/BepInEx/BepInEx/releases) for your respective system (for most Windows users, that will be the file called BepInEx_win_x64_X.X.X.X.zip)
- Install the core Archipelago tools from [Archipelago's Github Releases page](https://github.com/ArchipelagoMW/Archipelago/releases). On that page, scroll down to the "Assets" section for the release you want, click on the appropriate installer for your system to start downloading it (for most Windows users, that will be the file called Setup.Archipelago.X.Y.Z.exe), then run it.
- Go to the top of this repository. There should be five files: `LaikaMod.dll`, `Archipelago.MultiClient.Net.dll`, `Newtonsoft.Json.dll`, `laika_aged_through_blood.apworld`, and `LaikaAgedThroughBlood.yaml`. Download these five files.

### Archipelago tools setup
- Go to your Archipelago installation folder. Typically that will be `C:\ProgramData\Archipelago`.
- Put the `LaikaAgedThroughBlood.yaml` file in `Archipelago\Players`. You may leave the `.yaml` unchanged to play on default settings, or use your favorite text editor to read and change the settings in it.
- Double click on the `laika_aged_through_blood.apworld` file. Archipelago should display a popup saying it installed the apworld. Optionally, you can double-check that there's now an `laika_aged_through_blood.apworld` file in `Archipelago\custom_worlds\`.

**I've never used Archipelago before. How do I generate a multiworld?**
Let's create a randomized "multiworld" with only a single Laika: Aged Through Blood world in it.
- Make sure `LaikaAgedThroughBlood.yaml` is the only file in `Archipelago\Players` (subfolders here are fine).
- Double-click on `Archipelago\ArchipelagoGenerate.exe`. You should see a console window appear and then disappear after a few seconds.
- In `Archipelago\output\` there should now be a file with a name like `AP_95887452552422108902.zip`.
- Open https://archipelago.gg/uploads in your favorite web browser, and upload the output .zip you just generated. Click "Create New Room".
- The room page should give you a hostname and port number to connect to, e.g. "archipelago.gg:12345".

For a more complex multiworld, you'd put one `.yaml` file in the `\Players` folder for each world you want to generate. You can have multiple worlds of the same game (each with different options), as well as several different games, as long as each `.yaml` file has a unique player/slot name. It also doesn't matter who plays which game; it's common for one human player to play more than one game in a multiworld.

### Modding and Running Laika: Aged Through Blood
- Extract the downloaded BepInEx in your Laika:Aged Through Blood directory (default is C:\ProgramFiles (x86)\Steam\SteamApps\Common\Laika - Aged Through Blood). You can also right click on Laika in your Steam library > Properties > Installed Files > Browse and it will take you to the root folder.
- Launch the game once to allow BepInEx to install required folders. Once the game successfully boots, close the game.
- Return back to the root folder. Inside the newly extracted BepInEx, put all three of your downloaded .dll files (`LaikaMod.dll`, `Archipelago.MultiClient.Net.dll`, and `Newtonsoft.Json.dll`) inside the plugins folder (Should be ...Steam\steamapps\common\Laika - Aged Through Blood\BepInEx\plugins)
- Launch the game and hit the `Play` button. If you see "Archipelago Edition" under the title alongside a new panel, then congrats! You successfully installed the Laika Archipelago mod!

[This video](https://youtu.be/-YUsdD3nlbU) may assist with installation if you are unfamiliar with installing BepInEx or modding Unity games.

# Other Suggested Mods and Tools
PLACEHOLDER/NA

# Credits
![Credits Gif](Images&Icons/laikacreditsbanner.png)
- @ndubs103 for proposing the idea in the `future-game-design` thread on the Archipelago Discord server and assisting with the logic. They have been a great help!
- @ixrec for his inspirational support for the Nine Sols and Outer Wilds Archipelago. His work motivated me to look into working on AP modding (Plus, he's just a really cool cat!).
- @Rixor and everyone in *Paradise* who motivated me to continue working on the project.
- Everyone at *Brainwash Gang* who made this great game <3.
