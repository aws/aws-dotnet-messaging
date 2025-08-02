using BenchmarkDotNet.Running;

namespace AWS.Messaging.Benchmarks.Serialization;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<SerializationBenchmarks>();
    }
}
