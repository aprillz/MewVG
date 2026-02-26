namespace MewVG.Demo.MacOS;

internal static class Program
{
    private static void Main()
    {
        try
        {
            using var runner = new MetalDemoRunner();
            runner.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }
}
