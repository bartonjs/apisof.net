namespace Terrajobst.ApiCatalog.Features;

public sealed class FeatureUsageSource
{
    public FeatureUsageSource(string name, DateOnly date, int size)
    {
        ThrowIfNullOrEmpty(name);

        Name = name;
        Date = date;
        Size = size;
    }

    public string Name { get; }

    public DateOnly Date { get; }

    public int Size { get; }
}