```

BenchmarkDotNet v0.13.10, Windows 11 (10.0.26100.4652)
Unknown processor
.NET SDK 10.0.100-preview.2.25164.34
  [Host]     : .NET 8.0.17 (8.0.1725.26602), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.17 (8.0.1725.26602), X64 RyuJIT AVX2


```
| Method                                    | ItemCount | Mean         | Error       | StdDev      | Ratio | Gen0    | Gen1    | Gen2    | Allocated | Alloc Ratio |
|------------------------------------------ |---------- |-------------:|------------:|------------:|------:|--------:|--------:|--------:|----------:|------------:|
| **StandardSerializer**                        | **1**         |   **1,046.8 ns** |     **2.09 ns** |     **1.85 ns** |  **1.00** |  **0.1240** |       **-** |       **-** |    **2368 B** |        **1.00** |
| StandardSerializerWithJsonContext         | 1         |     931.3 ns |     1.58 ns |     1.32 ns |  0.89 |  0.1087 |       - |       - |    2056 B |        0.87 |
| JsonWriterSerializer                      | 1         |     480.6 ns |     2.31 ns |     2.16 ns |  0.46 |  0.0544 |       - |       - |    1040 B |        0.44 |
| JsonWriterSerializerWithJsonContext       | 1         |     376.9 ns |     0.68 ns |     0.64 ns |  0.36 |  0.0386 |       - |       - |     728 B |        0.31 |
| JsonWriterSerializerWithJsonContextUnsafe | 1         |     279.2 ns |     0.75 ns |     0.67 ns |  0.27 |  0.0386 |       - |       - |     728 B |        0.31 |
|                                           |           |              |             |             |       |         |         |         |           |             |
| **StandardSerializer**                        | **10**        |   **3,178.0 ns** |     **6.32 ns** |     **5.91 ns** |  **1.00** |  **0.2861** |       **-** |       **-** |    **5432 B** |        **1.00** |
| StandardSerializerWithJsonContext         | 10        |   2,640.6 ns |     7.66 ns |     7.16 ns |  0.83 |  0.2708 |       - |       - |    5120 B |        0.94 |
| JsonWriterSerializer                      | 10        |   1,274.2 ns |     4.00 ns |     3.74 ns |  0.40 |  0.1011 |       - |       - |    1920 B |        0.35 |
| JsonWriterSerializerWithJsonContext       | 10        |     817.5 ns |     2.35 ns |     2.20 ns |  0.26 |  0.0849 |       - |       - |    1608 B |        0.30 |
| JsonWriterSerializerWithJsonContextUnsafe | 10        |     618.9 ns |     1.66 ns |     1.55 ns |  0.19 |  0.0849 |       - |       - |    1608 B |        0.30 |
|                                           |           |              |             |             |       |         |         |         |           |             |
| **StandardSerializer**                        | **100**       |  **26,593.8 ns** |   **144.59 ns** |   **135.25 ns** |  **1.00** |  **1.9531** |  **0.1526** |       **-** |   **37032 B** |        **1.00** |
| StandardSerializerWithJsonContext         | 100       |  22,249.2 ns |    66.56 ns |    59.01 ns |  0.84 |  1.9226 |  0.1526 |       - |   36720 B |        0.99 |
| JsonWriterSerializer                      | 100       |   7,252.0 ns |    39.25 ns |    36.72 ns |  0.27 |  0.5875 |       - |       - |   11104 B |        0.30 |
| JsonWriterSerializerWithJsonContext       | 100       |   3,822.7 ns |    17.19 ns |    16.08 ns |  0.14 |  0.5722 |       - |       - |   10792 B |        0.29 |
| JsonWriterSerializerWithJsonContextUnsafe | 100       |   3,530.0 ns |     8.45 ns |     7.05 ns |  0.13 |  0.5722 |       - |       - |   10792 B |        0.29 |
|                                           |           |              |             |             |       |         |         |         |           |             |
| **StandardSerializer**                        | **1000**      | **311,337.5 ns** | **1,470.11 ns** | **1,303.22 ns** |  **1.00** | **96.6797** | **96.6797** | **96.6797** |  **361993 B** |        **1.00** |
| StandardSerializerWithJsonContext         | 1000      | 277,978.3 ns |   653.26 ns |   579.10 ns |  0.89 | 96.6797 | 96.6797 | 96.6797 |  361681 B |        1.00 |
| JsonWriterSerializer                      | 1000      | 103,399.2 ns |   234.08 ns |   207.51 ns |  0.33 | 33.3252 | 33.3252 | 33.3252 |  106538 B |        0.29 |
| JsonWriterSerializerWithJsonContext       | 1000      |  69,779.9 ns |   146.32 ns |   136.87 ns |  0.22 | 33.3252 | 33.3252 | 33.3252 |  106226 B |        0.29 |
| JsonWriterSerializerWithJsonContextUnsafe | 1000      |  70,258.7 ns |   208.98 ns |   195.48 ns |  0.23 | 33.3252 | 33.3252 | 33.3252 |  106226 B |        0.29 |
