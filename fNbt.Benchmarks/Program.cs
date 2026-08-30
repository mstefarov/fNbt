using BenchmarkDotNet.Configs;
using BenchmarkDotNet.ConsoleArguments;
using BenchmarkDotNet.ConsoleArguments.ListBenchmarks;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace fNbt.Benchmarks;

class Program {
    public const string BaselineIncompatible = "BaselineIncompatible";

    static int Main(string[] args) {
        var customArgs = new CustomArguments(args);
        var benchmarkArgs = customArgs.GetRemainingArgs();

        var logger = ConsoleLogger.Default;
        var initialConfig = DefaultConfig.Instance
            .AddDiagnoser(MemoryDiagnoser.Default);

        if (customArgs.BaselineSource != null && customArgs.BaselineVersion == null) {
            logger.WriteLineError("// --baseline-source has no effect without --baseline");
            return 1;
        }

        // Parse the BenchmarkDotNet options up front, to get at the parsed jobs below
        (bool isSuccess, IConfig parsedConfig, CommandLineOptions parsedOptions) = ConfigParser.Parse(benchmarkArgs, logger, initialConfig);
        if (!isSuccess)
            return IsHelpOrVersion(benchmarkArgs) ? 0 : 1;

        // Benchmarks using 2.0 APIs can't compile against a 1.x baseline package. Source #if
        // guards don't help, since BenchmarkDotNet generates boilerplate from the default build.
        if (customArgs.BaselineVersion != null && IsPre2(customArgs.BaselineVersion)) {
            initialConfig.AddFilter(new ExcludeCategoryFilter(BaselineIncompatible));
        }

        // When "--baseline" is specified, add the package comparison job.
        if (customArgs.BaselineVersion is string version) {
            // With no --job or --runtimes, the parsed config has no jobs yet. The switcher only
            // adds the default job if the config still has none, so add the local job here too.
            // Otherwise a plain --baseline run would silently skip the comparison.
            Job[] jobs = parsedConfig.GetJobs().ToArray();
            if (jobs.Length > 1) {
                logger.WriteLineError("// --baseline cannot be combined with multiple jobs or runtimes; run each comparison separately");
                return 1;
            }
            if (jobs.Length == 0) {
                jobs = new[] { Job.Default };
                initialConfig.AddJob(Job.Default);
            }
            foreach (Job job in jobs) {
                var msBuildArgs = new List<string> { $"/p:FNbtNuGetVersion={version}" };
                if (customArgs.BaselineSource is string source) {
                    msBuildArgs.Add($"/p:RestoreAdditionalProjectSources={source}");
                }
                initialConfig.AddJob(job
                    .WithId($"{job.Id}-NuGet")
                    .WithMsBuildArguments(msBuildArgs.ToArray())
                    .WithBaseline(true));
                logger.WriteLineInfo($"// Baseline job added: {job.Id}-NuGet (fNbt {version})");
            }
        }

        // Args go to the switcher itself, not into the config. Given an empty array, it ignores
        // --filter and drops into interactive selection.
        Summary[] summaries = BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(benchmarkArgs, initialConfig)
            .ToArray();

        if (parsedOptions.PrintInformation || parsedOptions.ListBenchmarkCaseMode != ListBenchmarkCaseMode.Disabled)
            return 0;

        if (summaries.Any(summary => summary.HasCriticalValidationErrors)) {
            logger.WriteLineError("// Benchmark run failed validation");
            return 1;
        }
        if (summaries.SelectMany(summary => summary.Reports).Any(report => !report.Success)) {
            logger.WriteLineError("// One or more benchmark reports failed");
            return 1;
        }
        if (summaries.Sum(summary => summary.GetNumberOfExecutedBenchmarks()) == 0) {
            logger.WriteLineError("// No benchmarks were executed");
            return 1;
        }
        return 0;
    }

    static bool IsHelpOrVersion(string[] args) {
        return args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--version", StringComparison.OrdinalIgnoreCase));
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
