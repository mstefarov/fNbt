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
        // Parse custom arguments
        var customArgs = new CustomArguments(args);
        var benchmarkArgs = customArgs.GetRemainingArgs();

        var logger = ConsoleLogger.Default;
        var initialConfig = DefaultConfig.Instance
            .AddDiagnoser(MemoryDiagnoser.Default);

        // Parse *all* BenchmarkDotNet options (runtimes, filters, diagnosers, etc.)
        (bool isSuccess, IConfig parsedConfig, CommandLineOptions options) = ConfigParser.Parse(benchmarkArgs, logger, initialConfig);
        if (!isSuccess)
            return;

        // Benchmarks using newer APIs cannot compile against the baseline, and guarding the source is not
        // enough: BenchmarkDotNet builds its boilerplate from the default build, so they must leave the run.
        if (customArgs.BaselineVersion != null) {
            initialConfig.AddFilter(new ExcludeCategoryFilter(BaselineIncompatible));
        }

        // When "--baseline" is specified, add a baseline for each job
        if (customArgs.BaselineVersion is string version) {
            bool isFirst = true;
            foreach (Job job in parsedConfig.GetJobs().ToArray()) {
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
                isFirst = false;
            }
        }

        // Args go to the switcher rather than into a config: given an empty array it ignores --filter
        // and drops into interactive selection.
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(benchmarkArgs, initialConfig);
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
