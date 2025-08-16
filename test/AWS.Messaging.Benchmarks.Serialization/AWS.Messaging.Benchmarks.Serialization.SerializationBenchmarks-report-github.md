```

BenchmarkDotNet v0.13.10, Windows 11 (10.0.26100.4652)
Unknown processor
.NET SDK 10.0.100-preview.2.25164.34
  [Host]     : .NET 8.0.17 (8.0.1725.26602), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.17 (8.0.1725.26602), X64 RyuJIT AVX2


```
| Method                                    | ItemCount | Mean         | Error     | StdDev    | Median       | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated | Alloc Ratio |
|------------------------------------------ |---------- |-------------:|----------:|----------:|-------------:|------:|--------:|--------:|--------:|--------:|----------:|------------:|
| **StandardSerializer**                        | **1**         |   **1,047.7 ns** |   **9.08 ns** |   **8.49 ns** |   **1,047.1 ns** |  **1.00** |    **0.00** |  **0.1240** |       **-** |       **-** |    **2368 B** |        **1.00** |
| StandardSerializerWithJsonContext         | 1         |     941.7 ns |   2.85 ns |   2.67 ns |     942.0 ns |  0.90 |    0.01 |  0.1087 |       - |       - |    2056 B |        0.87 |
| JsonWriterSerializer                      | 1         |     458.4 ns |   2.27 ns |   2.12 ns |     458.6 ns |  0.44 |    0.00 |  0.0548 |       - |       - |    1040 B |        0.44 |
| JsonWriterSerializerWithJsonContext       | 1         |     357.0 ns |   1.71 ns |   1.60 ns |     357.4 ns |  0.34 |    0.00 |  0.0386 |       - |       - |     728 B |        0.31 |
| JsonWriterSerializerWithJsonContextUnsafe | 1         |     276.4 ns |   0.64 ns |   0.57 ns |     276.3 ns |  0.26 |    0.00 |  0.0386 |       - |       - |     728 B |        0.31 |
|                                           |           |              |           |           |              |       |         |         |         |         |           |             |
| **StandardSerializer**                        | **10**        |   **3,162.9 ns** |  **19.17 ns** |  **17.93 ns** |   **3,168.6 ns** |  **1.00** |    **0.00** |  **0.2861** |       **-** |       **-** |    **5432 B** |        **1.00** |
| StandardSerializerWithJsonContext         | 10        |   2,617.0 ns |   6.00 ns |   5.61 ns |   2,616.1 ns |  0.83 |    0.01 |  0.2708 |       - |       - |    5120 B |        0.94 |
| JsonWriterSerializer                      | 10        |   1,112.1 ns |   5.10 ns |   4.26 ns |   1,112.6 ns |  0.35 |    0.00 |  0.1011 |       - |       - |    1920 B |        0.35 |
| JsonWriterSerializerWithJsonContext       | 10        |     696.2 ns |   2.06 ns |   1.92 ns |     695.8 ns |  0.22 |    0.00 |  0.0849 |       - |       - |    1608 B |        0.30 |
| JsonWriterSerializerWithJsonContextUnsafe | 10        |     598.4 ns |   4.37 ns |   4.09 ns |     599.8 ns |  0.19 |    0.00 |  0.0849 |       - |       - |    1608 B |        0.30 |
|                                           |           |              |           |           |              |       |         |         |         |         |           |             |
| **StandardSerializer**                        | **100**       |  **27,274.1 ns** | **491.14 ns** | **820.58 ns** |  **27,431.5 ns** |  **1.00** |    **0.00** |  **1.9531** |  **0.1526** |       **-** |   **37032 B** |        **1.00** |
| StandardSerializerWithJsonContext         | 100       |  19,778.3 ns | 389.58 ns | 702.49 ns |  19,639.0 ns |  0.73 |    0.04 |  1.9226 |  0.1526 |       - |   36720 B |        0.99 |
| JsonWriterSerializer                      | 100       |   7,880.9 ns | 157.43 ns | 303.32 ns |   7,744.5 ns |  0.29 |    0.01 |  0.5798 |       - |       - |   11104 B |        0.30 |
| JsonWriterSerializerWithJsonContext       | 100       |   4,023.4 ns |  80.33 ns | 222.61 ns |   3,955.4 ns |  0.15 |    0.01 |  0.5722 |       - |       - |   10792 B |        0.29 |
| JsonWriterSerializerWithJsonContextUnsafe | 100       |   3,617.6 ns |  35.00 ns |  32.74 ns |   3,600.0 ns |  0.13 |    0.01 |  0.5722 |       - |       - |   10792 B |        0.29 |
|                                           |           |              |           |           |              |       |         |         |         |         |           |             |
| **StandardSerializer**                        | **1000**      | **274,182.0 ns** | **752.42 ns** | **667.00 ns** | **274,142.0 ns** |  **1.00** |    **0.00** | **96.6797** | **96.6797** | **96.6797** |  **361993 B** |        **1.00** |
| StandardSerializerWithJsonContext         | 1000      | 238,880.0 ns | 441.12 ns | 391.04 ns | 238,938.5 ns |  0.87 |    0.00 | 96.6797 | 96.6797 | 96.6797 |  361681 B |        1.00 |
| JsonWriterSerializer                      | 1000      | 101,512.4 ns | 260.47 ns | 243.65 ns | 101,461.8 ns |  0.37 |    0.00 | 33.3252 | 33.3252 | 33.3252 |  106538 B |        0.29 |
| JsonWriterSerializerWithJsonContext       | 1000      |  69,257.1 ns | 267.03 ns | 222.98 ns |  69,241.4 ns |  0.25 |    0.00 | 33.3252 | 33.3252 | 33.3252 |  106226 B |        0.29 |
| JsonWriterSerializerWithJsonContextUnsafe | 1000      |  69,293.8 ns | 320.33 ns | 299.64 ns |  69,175.4 ns |  0.25 |    0.00 | 33.3252 | 33.3252 | 33.3252 |  106226 B |        0.29 |
