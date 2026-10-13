namespace FluxForge.Desktop.Services;

public static class Pages
{
    public const string Home = "home", Read = "read", Write = "write", Verify = "verify", ImageStudio = "studio",
        Library = "library", DriveTools = "drive", Activity = "activity", Settings = "settings";
}

public interface INavigator
{
    void Navigate(string page);
}
