# 📊 Benchmark Comparison: Vanilla vs Optimized Deserialization

> **Date**: 2026-04-15
> **Runtime**: .NET 8.0.25 (8.0.2526.11203), X64 RyuJIT AVX2
> **BenchmarkDotNet**: v0.15.2
> **OS**: Windows 11 (10.0.26200.8037)

## Branches

| Label | Branch | Description |
|-------|--------|-------------|
| **Vanilla** | `janhyka/jsonwriterserializer` | Baseline — no deserialization optimizations |
| **Optimized** | `janhyka/deserializationoptimizations` | All deserialization optimizations applied |

---

## Side-by-Side Results

### Small Payload (~200B JSON, 3 properties)

| Method | Vanilla Mean | Optimized Mean | Δ Mean | Speedup | Vanilla Alloc | Optimized Alloc | Δ Alloc | Alloc Reduction |
|--------|------------:|---------------:|-------:|--------:|--------------:|----------------:|--------:|----------------:|
| Deserialize_Envelope | 1,667.0 ns | 820.2 ns | −846.8 ns | **2.03×** | 2,519 B | 792 B | −1,727 B | **68.6%** |
| Deserialize_SNS_Wrapped | 3,108.0 ns | 2,291.1 ns | −816.9 ns | **1.36×** | 3,553 B | 1,104 B | −2,449 B | **68.9%** |
| Deserialize_EventBridge_Wrapped | 2,845.0 ns | 1,593.5 ns | −1,251.5 ns | **1.79×** | 3,840 B | 1,168 B | −2,672 B | **69.6%** |

### Medium Payload (~1KB JSON, 15 properties with nested objects/arrays)

| Method | Vanilla Mean | Optimized Mean | Δ Mean | Speedup | Vanilla Alloc | Optimized Alloc | Δ Alloc | Alloc Reduction |
|--------|------------:|---------------:|-------:|--------:|--------------:|----------------:|--------:|----------------:|
| Deserialize_Envelope | 4,370.0 ns | 2,564.1 ns | −1,805.9 ns | **1.70×** | 5,806 B | 2,376 B | −3,430 B | **59.1%** |
| Deserialize_SNS_Wrapped | 7,368.0 ns | 6,049.0 ns | −1,319.0 ns | **1.22×** | 7,659 B | 2,688 B | −4,971 B | **64.9%** |
| Deserialize_EventBridge_Wrapped | 5,970.0 ns | 3,778.6 ns | −2,191.4 ns | **1.58×** | 7,977 B | 2,752 B | −5,225 B | **65.5%** |

### Large Payload (~5KB JSON, 50+ properties with deep nesting, arrays of objects)

| Method | Vanilla Mean | Optimized Mean | Δ Mean | Speedup | Vanilla Alloc | Optimized Alloc | Δ Alloc | Alloc Reduction |
|--------|------------:|---------------:|-------:|--------:|--------------:|----------------:|--------:|----------------:|
| Deserialize_Envelope | 22,897.0 ns | 14,943.4 ns | −7,953.6 ns | **1.53×** | 28,252 B | 11,424 B | −16,828 B | **59.6%** |
| Deserialize_SNS_Wrapped | 36,467.0 ns | 33,081.0 ns | −3,386.0 ns | **1.10×** | 36,864 B | 11,736 B | −25,128 B | **68.1%** |
| Deserialize_EventBridge_Wrapped | 28,343.0 ns | 18,673.8 ns | −9,669.2 ns | **1.52×** | 37,151 B | 11,800 B | −25,351 B | **68.2%** |

---

## Summary

### Throughput (Mean Latency) Improvements

| Payload Size | Avg Speedup | Best Case |
|:-------------|:------------|:----------|
| **Small** | **1.73×** | 2.03× (Deserialize_Envelope) |
| **Medium** | **1.50×** | 1.70× (Deserialize_Envelope) |
| **Large** | **1.38×** | 1.53× (Deserialize_Envelope) |

### Memory Allocation Reductions

| Payload Size | Avg Reduction | Range |
|:-------------|:--------------|:------|
| **Small** | **69.0%** less | 68.6% – 69.6% |
| **Medium** | **63.1%** less | 59.1% – 65.5% |
| **Large** | **65.3%** less | 59.6% – 68.2% |

### Key Takeaways

1. **Direct envelope deserialization sees the largest speedups** (1.53×–2.03×), with the small payload benefiting the most from reduced overhead.
2. **Memory allocation reductions are dramatic and consistent** across all payload sizes, averaging ~65% less allocated bytes. This translates to significantly lower GC pressure under high-throughput workloads.
3. **SNS-wrapped messages show the smallest throughput improvement** (1.10×–1.36×), suggesting that the SNS unwrapping step dominates latency for those paths and is a potential target for further optimization.
4. **The optimizations scale well**: even at 5KB payloads with deep nesting and 10 nested employee objects, the optimized path delivers 1.38× average speedup with 65% less allocation.

---

## Raw Results

<details>
<summary>Vanilla Branch (janhyka/jsonwriterserializer)</summary>

| Method                          | Payload | Mean      | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|-------------------------------- |-------- |----------:|----------:|----------:|-------:|-------:|----------:|
| Deserialize_Envelope            | Small   |  1.667 μs | 0.0061 μs | 0.0057 μs | 0.1335 |      - |   2.46 KB |
| Deserialize_SNS_Wrapped         | Small   |  3.108 μs | 0.0090 μs | 0.0080 μs | 0.1869 |      - |   3.47 KB |
| Deserialize_EventBridge_Wrapped | Small   |  2.845 μs | 0.0530 μs | 0.0443 μs | 0.2022 |      - |   3.75 KB |
| Deserialize_Envelope            | Medium  |  4.370 μs | 0.0444 μs | 0.0394 μs | 0.3052 |      - |   5.67 KB |
| Deserialize_SNS_Wrapped         | Medium  |  7.368 μs | 0.0502 μs | 0.0469 μs | 0.4044 |      - |   7.48 KB |
| Deserialize_EventBridge_Wrapped | Medium  |  5.970 μs | 0.0449 μs | 0.0420 μs | 0.4196 |      - |   7.79 KB |
| Deserialize_Envelope            | Large   | 22.897 μs | 0.2258 μs | 0.2112 μs | 1.4954 | 0.1221 |  27.59 KB |
| Deserialize_SNS_Wrapped         | Large   | 36.467 μs | 0.0735 μs | 0.0651 μs | 1.9531 | 0.1831 |     36 KB |
| Deserialize_EventBridge_Wrapped | Large   | 28.343 μs | 0.0723 μs | 0.0604 μs | 1.9531 | 0.0916 |  36.28 KB |

</details>

<details>
<summary>Optimized Branch (janhyka/deserializationoptimizations)</summary>

| Method                          | Payload | Mean        | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|-------------------------------- |-------- |------------:|----------:|----------:|-------:|-------:|----------:|
| Deserialize_Envelope            | Small   |    820.2 ns |   9.66 ns |   9.04 ns | 0.0420 |      - |     792 B |
| Deserialize_SNS_Wrapped         | Small   |  2,291.1 ns |  15.31 ns |  14.32 ns | 0.0572 |      - |    1104 B |
| Deserialize_EventBridge_Wrapped | Small   |  1,593.5 ns |   4.61 ns |   4.09 ns | 0.0610 |      - |    1168 B |
| Deserialize_Envelope            | Medium  |  2,564.1 ns |   8.51 ns |   7.54 ns | 0.1259 |      - |    2376 B |
| Deserialize_SNS_Wrapped         | Medium  |  6,049.0 ns |  60.07 ns |  56.19 ns | 0.1373 |      - |    2688 B |
| Deserialize_EventBridge_Wrapped | Medium  |  3,778.6 ns |  22.48 ns |  21.03 ns | 0.1450 |      - |    2752 B |
| Deserialize_Envelope            | Large   | 14,943.4 ns |  28.57 ns |  25.32 ns | 0.5951 | 0.0153 |   11424 B |
| Deserialize_SNS_Wrapped         | Large   | 33,081.0 ns | 236.80 ns | 209.92 ns | 0.6104 |      - |   11736 B |
| Deserialize_EventBridge_Wrapped | Large   | 18,673.8 ns |  50.98 ns |  45.19 ns | 0.6104 |      - |   11800 B |

</details>
