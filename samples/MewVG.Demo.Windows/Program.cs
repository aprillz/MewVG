namespace MewVG.Demo.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            using var runner = new Win32DemoRunner();
            runner.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }
}
