try
{
    using var game = new RotBoiRemastered.Core.RotBoiGame();
    game.Run();
}
catch (Exception ex)
{
    // Unhandled crashes land in %TEMP%\rotboi_crash.txt so they can be
    // reported with a full stack trace.
    try
    {
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "rotboi_crash.txt"), ex.ToString());
    }
    catch
    {
        // Logging must never mask the original exception.
    }
    throw;
}
