﻿using System.Collections.Concurrent;

using NuGet.Frameworks;
using Terrajobst.ApiCatalog;
using Terrajobst.UsageCrawling;

internal static class ApiChecker
{
    public static void Run(ApiCatalogModel catalog,
                           IReadOnlyCollection<string> filePaths,
                           IReadOnlyList<NuGetFramework> frameworks,
                           bool analyzeObsoletion,
                           IReadOnlyList<string> platforms,
                           Action<AssemblyResult> resultReceiver)
    {
        var apiByGuid = catalog.AllApis.ToDictionary(a => a.Guid);

        var platformContexts = frameworks.Select(fx => PlatformAnnotationContext.Create(catalog, fx.GetShortFolderName()))
                                         .ToDictionary(pc => pc.Framework);

        foreach (var api in catalog.AllApis)
        {
            var forwardedApi = catalog.GetForwardedApi(api);
            if (forwardedApi is not null)
                apiByGuid[api.Guid] = forwardedApi.Value;
        }

        var apiAvailability = new ConcurrentDictionary<ApiModel, ApiAvailability>();

        var resultSink = new BlockingCollection<AssemblyResult>();

        var resultSinkTask = Task.Run(() =>
        {
            foreach (var result in resultSink.GetConsumingEnumerable())
                resultReceiver(result);
        });

        Parallel.ForEach(filePaths, filePath =>
        {
            string assemblyName;

            if (LibraryReader.TryOpen(filePath, out var libraryReader))
            {
                assemblyName = libraryReader.MetadataReader.GetString(
                    libraryReader.MetadataReader.GetAssemblyDefinition().Name);
            }
            else
            {
                assemblyName = Path.GetFileName(filePath);
            }

            if (libraryReader is null)
            {
                var result = new AssemblyResult(assemblyName, "Not a valid .NET assembly", Array.Empty<ApiResult>());
                resultSink.Add(result);
            }
            else
            {
                //var assemblyTfm = libraryReader.GetTargetFrameworkMoniker();
                //var assemblyFramework = string.IsNullOrEmpty(assemblyTfm) ? null : NuGetFramework.Parse(assemblyTfm);
                NuGetFramework? assemblyFramework = null;

                var crawler = new AssemblyCrawler();
                crawler.Crawl(libraryReader);

                var crawlerResults = crawler.GetResults();

                var apiResults = new List<ApiResult>();
                var frameworkResultBuilder = new List<FrameworkResult>(frameworks.Count);
                var platformResultBuilder = new List<PlatformResult?>(platforms.Count);

                foreach (var apiKey in crawlerResults.Data)
                {
                    if (apiByGuid.TryGetValue(apiKey.Guid, out var api))
                    {
                        var availability = apiAvailability.GetOrAdd(api, a => a.GetAvailability());

                        frameworkResultBuilder.Clear();

                        foreach (var framework in frameworks)
                        {
                            // Analyze availability

                            AvailabilityResult availabilityResult;

                            var infos = availability.Frameworks.Where(fx => fx.Framework == framework).ToArray();

                            // NOTE: There are APIs that exist in multiple places in-box, e.g. Microsoft.Windows.Themes.ListBoxChrome.
                            //       It doesn't really matter for our purposes. Either way, we'll pick the first one.
                            var info = infos.FirstOrDefault(i => i.IsInBox) ?? infos.FirstOrDefault(i => !i.IsInBox);

                            if (info is null)
                            {
                                availabilityResult = AvailabilityResult.Unavailable;
                            }
                            else if (info.IsInBox)
                            {
                                availabilityResult = AvailabilityResult.AvailableInBox;
                            }
                            else
                            {
                                var package = info.PackageDeclarations.First().Package;
                                availabilityResult = AvailabilityResult.AvailableInPackage(package);
                            }

                            // Analyze obsoletion

                            ObsoletionResult? obsoletionResult;

                            if (!analyzeObsoletion || info?.Declaration.Obsoletion is null)
                            {
                                obsoletionResult = null;
                            }
                            else
                            {
                                var compiledAgainstObsoleteApi = false;

                                if (assemblyFramework is not null)
                                {
                                    var compiledAvailability = api.GetAvailability(assemblyFramework);
                                    if (compiledAvailability?.Declaration.Obsoletion is not null)
                                        compiledAgainstObsoleteApi = true;
                                }

                                if (compiledAgainstObsoleteApi)
                                {
                                    obsoletionResult = null;
                                }
                                else
                                {
                                    var o = info.Declaration.Obsoletion.Value;
                                    obsoletionResult = new ObsoletionResult(o.Message, o.Url);
                                }
                            }

                            // Analyze platform support

                            platformResultBuilder.Clear();

                            if (info is null)
                            {
                                for (var i = 0; i < platforms.Count; i++)
                                    platformResultBuilder.Add(null);
                            }
                            else
                            {
                                var platformContext = platformContexts[framework];
                                foreach (var platform in platforms)
                                {
                                    var annotation = platformContext.GetPlatformAnnotation(api);
                                    var isSupported = annotation.IsSupported(platform);
                                    var platformResult = isSupported ? PlatformResult.Supported : PlatformResult.Unsupported;
                                    platformResultBuilder.Add(platformResult);
                                }
                            }

                            var frameworkResult = new FrameworkResult(availabilityResult, obsoletionResult, platformResultBuilder.ToArray());
                            frameworkResultBuilder.Add(frameworkResult);
                        }

                        var apiResult = new ApiResult(api, frameworkResultBuilder.ToArray());
                        apiResults.Add(apiResult);
                    }
                }

                var results = new AssemblyResult(assemblyName, null, apiResults.ToArray());
                resultSink.Add(results);
            }
        });

        resultSink.CompleteAdding();
        resultSinkTask.Wait();
    }
}