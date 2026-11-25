# JagexManager

A Windows app for playing on more than one Jagex account. Sign in to each
account once, then start any character in RuneLite or the official clients with
one click.

JagexManager is unofficial and not affiliated with Jagex or RuneLite. It signs
in the same way the Jagex Launcher does, which Jagex could change at any time.

## Features

- Sign in on Jagex's own login page. JagexManager never sees or stores your
  password.
- Play any character in RuneLite, the official Old School client, RuneScape 3,
  or any other client you add.
- Run several characters at once, from one account or many. Start all of an
  account's shown characters, or a group you made from any accounts, with one
  click. Clients start one at a time so your PC keeps up.
- Bring a running character's game window to the front, or stop it.
- Choose a different client, launch options and RuneLite profile for each
  character, give characters your own names, and hide the ones you never play.
- Search your characters, see when each was last played, and sort by it.
- See whether each character is P2P, with the days of membership left, or F2P.
  The days turn amber in the last three days of a membership that does not
  renew, and JagexManager warns you when that starts.
- Start characters and groups from the notification area icon.
- Open the Jagex Launcher signed in to any of your accounts, or sign it out.
- Use RuneLite's colours and the RuneScape font.

## Install

Download `JagexManager.exe` from [Releases](../../releases) and run it. It needs
Windows 10 or 11 with RuneLite or the Jagex Launcher installed.

## Usage

1. Click **Add account** and sign in.
2. Click **Play** next to a character.
3. To use a different client for a character, click the arrow next to Play and
   pick one. It is remembered for that character.
4. Right-click a character, or use the same arrow, to rename it, give it its
   own launch options (such as `--profile=Iron`), pick one of your RuneLite
   profiles, add it to a group, or hide it. Launch options belong to the client
   they were set for, and are cleared if you pick another client for that
   character.
5. Groups appear at the top of the list. Click **Play** on a group to start
   all of its characters, or right-click it to rename or delete it.
6. While a character is running, click the window button next to **Stop** to
   bring its game window to the front.
7. With six or more characters, a search box appears above the list. Press
   Ctrl+F to jump to it and Esc to clear it.
8. To open the Jagex Launcher signed in to an account, click the
   **Open in the Jagex Launcher** icon on that account. If the launcher is
   already open, it is closed and reopened.
9. To use a character in another program, right-click it and choose
   **Copy session details**. This copies its `JX_SESSION_ID`, `JX_CHARACTER_ID`
   and `JX_DISPLAY_NAME`. Anyone with these can play as that character until the
   session ends, so only paste them into programs you trust.

The **Settings** tab has the default client and its launch options (such as
RuneLite's `--profile=Main`), how long to wait between clients when starting
several, the theme, the font, how to sort characters, starting with Windows,
minimising to the notification area, the membership warning, and a button to
sign the Jagex Launcher out.

When JagexManager is minimised, right-click its notification area icon to start
a character or group without opening the window. Click a running character
there to bring its window to the front.

Accounts are checked again when you come back to JagexManager after five
minutes, and every hour while it is open, so new characters and membership days
stay current.

JagexManager asks GitHub for the latest release each time it starts and shows a
notice when there is a newer one. It never downloads anything by itself. Turn
this off with **Check for new versions** in Settings.

Accounts are saved encrypted in `%LOCALAPPDATA%\JagexManager`.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.400 or
later) on Windows.

```powershell
dotnet build
dotnet run --project src/JagexManager
```

## Credits

The sign-in flow follows the open-source launchers
[Bolt](https://codeberg.org/Adamcake/Bolt) and
[golt](https://github.com/chowder/golt).

## License

[MIT](LICENSE). The included Cinzel and Inter fonts are under the SIL Open Font
License. The RuneScape font isn't included; JagexManager copies it from your
RuneLite install. RuneScape and Jagex are trademarks of Jagex Ltd.
