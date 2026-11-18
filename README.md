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
- Run several characters at once, from one account or many.
- Choose a different client for each character.
- See whether each character is P2P, with the days of membership left, or F2P.
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
4. To open the Jagex Launcher signed in to an account, click the
   **Open in the Jagex Launcher** icon on that account. If the launcher is
   already open, it is closed and reopened.
5. To use a character in another program, right-click it and choose
   **Copy session details**. This copies its `JX_SESSION_ID`, `JX_CHARACTER_ID`
   and `JX_DISPLAY_NAME`. Anyone with these can play as that character until the
   session ends, so only paste them into programs you trust.

The **Settings** tab has the default client and its launch options (such as
RuneLite's `--profile=Main`), the theme, the font, and a button to sign the
Jagex Launcher out.

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
