using BenchmarkDotNet.Configs;
using BenchmarkDotNet.ConsoleArguments;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

namespace fNbt.Benchmarks;

class Program {
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

        // When "--baseline" is specified, add a baseline for each job
        if (customArgs.BaselineVersion is string version) {
            bool isFirst = true;
            var jobList = (List<Job>)parsedConfig.GetJobs();
            foreach (Job job in jobList.ToArray()) {
                jobList[jobList.IndexOf(job)] = job.WithBaseline(false).Freeze(); // HACK: remove baseline flag from original job
                initialConfig.AddJob(job
                    .WithId($"{job.Id}-NuGet")
                    .WithNuGet("fNbt", version)
                    .WithBaseline(isFirst));
                isFirst = false;
            }
        }

        var finalConfig = ManualConfig.Union(initialConfig, parsedConfig);

        // Run benchmarks
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(Array.Empty<string>(), finalConfig);
    }
}

public class CustomArguments {
    public string? BaselineVersion { get; private set; }
    private readonly string[] remainingArgs;

    public CustomArguments(string[] args) {
        var argsList = args.ToList();

        // Parse --baseline argument
        var baselineIndex = argsList.FindIndex(a =>
            a.Equals("--baseline", StringComparison.OrdinalIgnoreCase));

        if (baselineIndex >= 0 && baselineIndex + 1 < argsList.Count) {
            BaselineVersion = argsList[baselineIndex + 1];
            argsList.RemoveRange(baselineIndex, 2);
        }

        remainingArgs = argsList.ToArray();
    }

    public string[] GetRemainingArgs() => remainingArgs;
}