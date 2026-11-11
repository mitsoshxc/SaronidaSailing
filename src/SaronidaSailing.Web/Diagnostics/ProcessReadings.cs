namespace SaronidaSailing.Web.Diagnostics;

public readonly record struct ProcessSample(
    long WorkingSetBytes,
    long GcHeapBytes,
    long MemoryLimitBytes,
    long GcHeapLimitBytes,
    TimeSpan CpuTime,
    int Cpus,
    int Gc0,
    int Gc1,
    int Gc2,
    int Threads,
    long QueueLength);

public interface IProcessReadings
{
    ProcessSample Read();
}

public sealed class ProcessReadings : IProcessReadings
{
    public ProcessSample Read() => new(
        Environment.WorkingSet,
        GC.GetTotalMemory(forceFullCollection: false),
        ContainerMemoryLimit.Bytes,
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        Environment.CpuUsage.TotalTime,
        Environment.ProcessorCount,
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2),
        ThreadPool.ThreadCount,
        ThreadPool.PendingWorkItemCount);
}
