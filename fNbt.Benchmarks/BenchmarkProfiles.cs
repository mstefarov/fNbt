using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

namespace fNbt.Benchmarks;

[AttributeUsage(AttributeTargets.Method)]
sealed class VeryStableBenchmarkAttribute : BenchmarkCategoryAttribute, IConfigSource {
    public VeryStableBenchmarkAttribute() : base(Program.VeryStable) {
        TimeInterval iterationTime = TimeInterval.Millisecond * 250;
        Config = ManualConfig.CreateEmpty().AddJob(Job.Default
            .WithLaunchCount(3)
            .WithIterationTime(iterationTime)
            .WithMinIterationTime(iterationTime)
            .WithMinIterationCount(15)
            .WithMaxIterationCount(20)
            .WithMaxRelativeError(0.01)
            .WithUnrollFactor(1)
            .WithOutlierMode(OutlierMode.RemoveUpper)
            .AsMutator());
    }

    public IConfig Config { get; }
}

[AttributeUsage(AttributeTargets.Method)]
sealed class AverageBenchmarkAttribute : BenchmarkCategoryAttribute, IConfigSource {
    public AverageBenchmarkAttribute(bool tieringSensitive = false) : base(Program.Average) {
        Job job = Job.Default
            .WithLaunchCount(3)
            .WithMinIterationCount(15)
            .WithMaxIterationCount(30)
            .WithMaxRelativeError(0.01)
            .WithOutlierMode(OutlierMode.RemoveUpper);
        if (tieringSensitive) {
            // This workload crosses the Dynamic PGO threshold during BDN's pilot stage. Give it
            // enough calls and warmup iterations to measure the optimized plateau consistently.
            job = job
                .WithMinInvokeCount(4096)
                .WithWarmupCount(15);
        }
        Config = ManualConfig.CreateEmpty().AddJob(job.AsMutator());
    }

    public IConfig Config { get; }
}

[AttributeUsage(AttributeTargets.Method)]
sealed class UnstableBenchmarkAttribute : BenchmarkCategoryAttribute, IConfigSource {
    public UnstableBenchmarkAttribute() : base(Program.Unstable) {
        // Extra iterations do not cure the process/layout modes in this group. Keep this profile
        // short and retain every observation: these timings are diagnostics, never gates.
        Config = ManualConfig.CreateEmpty().AddJob(Job.Default
            .WithLaunchCount(3)
            .WithMinIterationCount(15)
            .WithMaxIterationCount(20)
            .WithMaxRelativeError(0.02)
            .WithOutlierMode(OutlierMode.DontRemove)
            .AsMutator());
    }

    public IConfig Config { get; }
}
