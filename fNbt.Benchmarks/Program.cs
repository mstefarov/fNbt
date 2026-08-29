using BenchmarkDotNet.Configs;
using BenchmarkDotNet.ConsoleArguments;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

namespace fNbt.Benchmarks;

class Program {
    public const string BaselineIncompatible = "BaselineIncompatible";

    static void Main(string[] args) {
        var customArgs = new CustomArguments(args);
        var benchmarkArgs = customArgs.GetRemainingArgs();

        var logger = ConsoleLogger.Default;
        var initialConfig = DefaultConfig.Instance
            .AddDiagnoser(MemoryDiagnoser.Default);

        if (customArgs.BaselineSource != null && customArgs.BaselineVersion == null) {
            logger.WriteLineError("// --baseline-source has no effect without --baseline");
        }

        // Parse the BenchmarkDotNet options up front, to get at the parsed jobs below
        (bool isSuccess, IConfig parsedConfig, _) = ConfigParser.Parse(benchmarkArgs, logger, initialConfig);
        if (!isSuccess)
            return;

        // Benchmarks using 2.0 APIs can't compile against a 1.x baseline package. Source #if
        // guards don't help, since BenchmarkDotNet generates boilerplate from the default build.
        if (customArgs.BaselineVersion != null && IsPre2(customArgs.BaselineVersion)) {
            initialConfig.AddFilter(new ExcludeCategoryFilter(BaselineIncompatible));
        }

        // When "--baseline" is specified, add a baseline for each job
        if (customArgs.BaselineVersion is string version) {
            // With no --job or --runtimes, the parsed config has no jobs yet. The switcher only
            // adds the default job if the config still has none, so add the local job here too.
            // Otherwise a plain --baseline run would silently skip the comparison.
            Job[] jobs = parsedConfig.GetJobs().ToArray();
            if (jobs.Length == 0) {
                jobs = new[] { Job.Default };
                initialConfig.AddJob(Job.Default);
            }
            bool isFirst = true;
            foreach (Job job in jobs) {
                // With several --runtimes, BenchmarkDotNet already makes the first one the baseline, and
                // a group may only have one.
                bool makeBaseline = isFirst && !job.Meta.Baseline;
                var msBuildArgs = new List<string> { $"/p:FNbtNuGetVersion={version}" };
                if (customArgs.BaselineSource is string source) {
                    msBuildArgs.Add($"/p:RestoreAdditionalProjectSources={source}");
                }
                initialConfig.AddJob(job
                    .WithId($"{job.Id}-NuGet")
                    .WithMsBuildArguments(msBuildArgs.ToArray())
                    .WithBaseline(makeBaseline));
                logger.WriteLineInfo($"// Baseline job added: {job.Id}-NuGet (fNbt {version})");
                isFirst = false;
            }
        }

        // Args go to the switcher itself, not into the config. Given an empty array, it ignores
        // --filter and drops into interactive selection.
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(benchmarkArgs, initialConfig);
    }

    // Mirrors the FNBT_BASELINE condition in the csproj
    static bool IsPre2(string version) {
        return int.TryParse(version.Split('.')[0], out int major) && major < 2;
    }
}

public class CustomArguments {
    public string? BaselineVersion { get; private set; }
    public string? BaselineSource { get; private set; }
    private readonly string[] remainingArgs;

    public CustomArguments(string[] args) {
        var argsList = args.ToList();
        BaselineVersion = TakeValue(argsList, "--baseline");

        // A folder of .nupkg files, so the baseline can be a local build.
        string? source = TakeValue(argsList, "--baseline-source");
        if (source != null) {
            BaselineSource = Path.GetFullPath(source);
        }

        remainingArgs = argsList.ToArray();
    }

    static string? TakeValue(List<string> args, string name) {
        int index = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Count) {
            return null;
        }
        string value = args[index + 1];
        if (value.StartsWith("-", StringComparison.Ordinal)) {
            // Missing value: don't eat the next option. The flag stays behind, so
            // BenchmarkDotNet reports it as an unknown option instead of running wrong.
            return null;
        }
        args.RemoveRange(index, 2);
        return value;
    }

    public string[] GetRemainingArgs() => remainingArgs;
}

sealed class ExcludeCategoryFilter : IFilter {
    readonly string category;

    public ExcludeCategoryFilter(string category) {
        this.category = category;
    }

    public bool Predicate(BenchmarkCase benchmarkCase) {
        foreach (string c in benchmarkCase.Descriptor.Categories) {
            if (string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
