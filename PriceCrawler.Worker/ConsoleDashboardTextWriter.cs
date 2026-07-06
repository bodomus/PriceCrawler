using System.Text;

namespace PriceCrawler.Worker;

internal sealed class ConsoleDashboardTextWriter(TextWriter inner, object syncRoot) : TextWriter
{
    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        lock (syncRoot)
        {
            inner.Write(value);
        }
    }

    public override void Write(string? value)
    {
        lock (syncRoot)
        {
            inner.Write(value);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (syncRoot)
        {
            inner.WriteLine(value);
        }
    }

    public override void Flush()
    {
        lock (syncRoot)
        {
            inner.Flush();
        }
    }
}
