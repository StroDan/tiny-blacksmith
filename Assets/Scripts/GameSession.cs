// How the workshop scene should start, set by the main menu just before it loads the workshop.
// Continue (the default) is the old behaviour - load the save if there is one - so pressing Play on the
// workshop scene directly in the Editor, and the headless tests, work exactly as before.
public enum StartMode { Continue, NewGame, LoadGame }

public static class GameSession
{
    public const string MenuScene = "MainMenu";
    public const string WorkshopScene = "Prototype1";

    public static StartMode Mode = StartMode.Continue;
}
