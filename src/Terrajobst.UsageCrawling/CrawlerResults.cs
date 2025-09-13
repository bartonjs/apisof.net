namespace Terrajobst.UsageCrawling;

public sealed class CrawlerResults
{
    public CrawlerResults(IReadOnlySet<ApiKey> data)
    {
        Data = data;
    }

    public IReadOnlySet<ApiKey> Data { get; }
}