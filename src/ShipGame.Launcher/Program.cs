// Keeps the game up to date: checks GitHub for the latest release, installs it if it's new, and starts it.
// Arguments are passed through to the game (e.g. --connect host).
using var launcher = new ShipGame.Launcher.LauncherGame(args);
launcher.Run();
