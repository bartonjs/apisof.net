using System.Reflection;
using System.Reflection.Metadata;
using NuGet.Frameworks;

namespace Terrajobst.UsageCrawling.Collectors;

public sealed class TargetFrameworkCollector : IncrementalUsageCollector
{
    public override int VersionRequired => 5;

    protected override void CollectFeatures(LibraryReader libraryReader, AssemblyContext assemblyContext, Context context)
    {
        var framework = assemblyContext.Framework ?? InferFramework(libraryReader.MetadataReader);
        if (framework is not null)
            context.Report(FeatureUsage.ForTargetFramework(framework));
    }

    private static NuGetFramework? InferFramework(MetadataReader metadataReader)
    {
        var tfm = MetadataUtils.GetTargetFrameworkMoniker(metadataReader);

        return !string.IsNullOrEmpty(tfm)
            ? NuGetFramework.Parse(tfm)
            : InferFrameworkFromReferences(metadataReader);
    }

    private static NuGetFramework? InferFrameworkFromReferences(MetadataReader metadataReader)
    {
        foreach (var asmRefHandle in metadataReader.AssemblyReferences)
        {
            var assemblyReference = metadataReader.GetAssemblyReference(asmRefHandle);

            var name = metadataReader.GetString(assemblyReference.Name);
            var major = assemblyReference.Version.Major;
            var minor = assemblyReference.Version.Minor;
            var build = assemblyReference.Version.Build;

            if (string.Equals(name, "System.Runtime", StringComparison.OrdinalIgnoreCase))
            {
                if (major >= 5)
                    return NuGetFramework.Parse($"netcoreapp{major}.{minor}");

                switch (major, minor, build)
                {
                    case (4, 1, _):
                        return NuGetFramework.Parse("netcoreapp1.0");
                    case (4, 2, 1):
                        return NuGetFramework.Parse("netcoreapp2.1");
                    case (4, 2, 2):
                        return NuGetFramework.Parse("netcoreapp3.1");
                    case (4, 2, _):
                        return NuGetFramework.Parse("netcoreapp2.0");
                    case (4, 0, 10):
                        return NuGetFramework.Parse("netstandard1.2");
                    case (4, 0, 20):
                        return NuGetFramework.Parse("netstandard1.3");
                    case (4, 0, _):
                        return NuGetFramework.Parse("netstandard1.0");
                }
            }

            if (string.Equals(name, "netstandard", StringComparison.OrdinalIgnoreCase))
                return NuGetFramework.Parse($"netstandard{major}.{minor}");

            if ((assemblyReference.Flags & AssemblyFlags.PublicKey) == 0 &&
                string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var pktReader = metadataReader.GetBlobReader(assemblyReference.PublicKeyOrToken);
                    if (pktReader.Length != 8)
                    {
                        continue;
                    }

                    var token = pktReader.ReadBytes(pktReader.Length);

                    if (token is [0xB7, 0x7A, 0x5C, 0x56, 0x19, 0x34, 0xe0, 0x89])
                    {
                        switch (major, minor, build)
                        {
                            case (1, 0, 3300):
                                return NuGetFramework.Parse("net1.0");
                            case (1, 0, 5000):
                                return NuGetFramework.Parse("net2.0");
                            case (2, _, _):
                                return NuGetFramework.Parse("net2.0");
                            case (4, _, _):
                                return NuGetFramework.Parse("net4.0");
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                }
            }
        }

        return null;
    }
}