using System.Collections.Immutable;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.ConsoleArguments;
using BenchmarkDotNet.ConsoleArguments.ListBenchmarks;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace fNbt.Benchmarks;

class Program {
    public const string BaselineIncompatible = "BaselineIncompatible";
    public const string VeryStable = "VeryStable";
    public const string Average = "Average";
    public const string Unstable = "Unstable";

    static int Main(string[] args) {
        var customArgs = new CustomArguments(args);
        var benchmarkArgs = customArgs.GetRemainingArgs();

        var logger = ConsoleLogger.Default;
        var initialConfig = DefaultConfig.Instance
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(CsvMeasurementsExporter.Default)
            .AddColumn(CategoriesColumn.Default, StatisticColumn.MValue);

        if (customArgs.BaselineSource != null && customArgs.BaselineVersion == null) {
            logger.WriteLineError("// --baseline-source has no effect without --baseline");
            return 1;
        }
        if (customArgs.ReverseOrder) {
            initialConfig = initialConfig.WithOrderer(ReverseOrderer.Orderer);
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

        if (customArgs.ServerGc && customArgs.BaselineVersion == null) {
            // The GC mode must land in each generated app's runtimeconfig, which only a job
            // can do (BDN's runtimeconfig overrides GC environment variables). Package
            // comparisons build their jobs below; support only that shape.
            logger.WriteLineError("// --server-gc requires --baseline");
            return 1;
        }

        // When "--baseline" is specified, add the package comparison job.
        if (customArgs.BaselineVersion is string version) {
            // Settings such as --launchCount are represented as a mutator job. Give that mutator
            // two runnable jobs to modify; cloning it directly leaves only the NuGet job runnable.
            Job[] parsedJobs = parsedConfig.GetJobs().ToArray();
            if (parsedJobs.Length > 1) {
                logger.WriteLineError("// --baseline cannot be combined with multiple jobs or runtimes; run each comparison separately");
                return 1;
            }
            Job job = parsedJobs.Length == 0 || parsedJobs[0].Meta.IsMutator
                ? Job.Default
                : parsedJobs[0];
            if (customArgs.ServerGc) {
                job = job.WithGcServer(true);
                logger.WriteLineInfo("// Server GC scenario: jobs run with GcServer=true");
            }
            initialConfig.AddJob(job);
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
    public bool ReverseOrder { get; private set; }
    public bool ServerGc { get; private set; }
    private readonly string[] remainingArgs;

    public CustomArguments(string[] args) {
        var argsList = args.ToList();
        BaselineVersion = TakeValue(argsList, "--baseline");
        ReverseOrder = TakeSwitch(argsList, "--reverse-order");
        ServerGc = TakeSwitch(argsList, "--server-gc");

        // A folder of .nupkg files, so the baseline can be a local build.
        string? source = TakeValue(argsList, "--baseline-source");
        if (source != null) {
            BaselineSource = Path.GetFullPath(source);
        }

        remainingArgs = argsList.ToArray();
    }

    static bool TakeSwitch(List<string> args, string name) {
        int index = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) {
            return false;
        }
        args.RemoveAt(index);
        return true;
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

sealed class ReverseOrderer : DefaultOrderer {
    public static readonly IOrderer Orderer = new ReverseOrderer();

    public override IEnumerable<BenchmarkCase> GetExecutionOrder(ImmutableArray<BenchmarkCase> benchmarkCases,
                                                                   IEnumerable<BenchmarkLogicalGroupRule>? order = null) {
        return base.GetExecutionOrder(benchmarkCases, order).Reverse();
    }
}
