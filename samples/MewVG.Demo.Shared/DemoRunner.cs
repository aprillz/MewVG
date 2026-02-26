namespace MewVG.Demo;

internal abstract class DemoRunner : IDisposable
{
    protected const int DefaultWidth = 1900;
    protected const int DefaultHeight = 1000;
    protected const string DefaultTitle = "MewVG Demo";

    protected abstract void Initialize();
    protected abstract void Execute();
    protected abstract void Shutdown();
    public abstract void Dispose();

    public void Run()
    {
        Initialize();
        try
        {
            Execute();
        }
        finally
        {
            Shutdown();
        }
    }
}
