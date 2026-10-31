namespace RoweRC;

public sealed class SessionLog
{
    public string Path { get; }
    public SessionLog(string root) { Directory.CreateDirectory(root); Path = System.IO.Path.Combine(root, "session.log"); }
    public void Write(string message)
    {
        try
        {
            if (File.Exists(Path) && new FileInfo(Path).Length > 4 * 1024 * 1024) File.Move(Path, Path + ".previous", true);
            File.AppendAllText(Path, DateTimeOffset.Now.ToString("O") + " " + message.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
