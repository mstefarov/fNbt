using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

namespace fNbt.Benchmarks;

// These profiles assume the wrapper's affinity: one physical core with both of its logical
// CPUs, the second one for the runtime's own threads. On a single CPU the tiering worker
// competes with the measured thread and the runtime multiplies its tiering delay by ten, so
// tier-1 code arrived about two seconds in, after the pilot had sized the iteration on tier-0
// speed. Launch-to-launch spread (JIT and heap layout, Dynamic PGO outcomes) dominates the
// ratio noise on every row, so the two gating profiles spend their time on four launches
// rather than on long actual stages: the relative-error stop lands between 10 and 20 iterations.

[AttributeUsage(AttributeTargets.Method)]
sealed class VeryStableBenchmarkAttribute : BenchmarkCategoryAttribute, IConfigSource {
    public VeryStableBenchmarkAttribute() : base(Program.VeryStable) {
        TimeInterval iterationTime = TimeInterval.Millisecond * 250;
        Config = ManualConfig.CreateEmpty().AddJob(Job.Default
            .WithLaunchCount(4)
            .WithIterationTime(iterationTime)
            .WithMinIterationTime(iterationTime)
            .WithMinIterationCount(10)
            .WithMaxIterationCount(20)
            .WithMaxRelativeError(0.02)
            .WithUnrollFactor(1)
            .WithOutlierMode(OutlierMode.RemoveUpper)
            .AsMutator());
    }

    public IConfig Config { get; }
}

[AttributeUsage(AttributeTargets.Method)]
sealed class AverageBenchmarkAttribute : BenchmarkCategoryAttribute, IConfigSource {
    public AverageBenchmarkAttribute() : base(Program.Average) {
        // An explicit iteration time keeps the pilot from doubling past it, which used to leave
        // these rows with iterations of up to a second.
        Config = ManualConfig.CreateEmpty().AddJob(Job.Default
            .WithLaunchCount(4)
            .WithIterationTime(TimeInterval.Millisecond * 500)
            .WithMinIterationCount(10)
            .WithMaxIterationCount(20)
            .WithMaxRelativeError(0.02)
            .WithOutlierMode(OutlierMode.RemoveUpper)
            .AsMutator());
    }

    public IConfig Config { get; }
}

[AttributeUsage(AttributeTargets.Method)]
sealed class UnstableBenchmarkAttribute : BenchmarkCategoryAttribute, IConfigSource {
    public UnstableBenchmarkAttribute() : base(Program.Unstable) {
        // Extra iterations do not cure the memory-state modes in this group. Keep this profile
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
