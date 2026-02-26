namespace MewVG.Demo.Linux;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            using var runner = new X11DemoRunner();
            runner.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }
}
