```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
Unknown processor
.NET SDK 10.0.102
  [Host]     : .NET 10.0.2 (10.0.225.61305), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.2 (10.0.225.61305), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | RequestTypeCount | Distribution | Mean      | Error    | StdDev   | Ratio | RatioSD | Rank | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |----------------- |------------- |----------:|---------:|---------:|------:|--------:|-----:|-------:|----------:|------------:|
| **Custom_Send_Object_Polymorphic**  | **10**               | **Uniform**      |  **52.93 ns** | **0.883 ns** | **0.826 ns** |  **1.00** |    **0.02** |    **1** | **0.0157** |     **264 B** |        **1.00** |
| MediatR_Send_Object_Polymorphic | 10               | Uniform      | 119.51 ns | 0.629 ns | 0.588 ns |  2.26 |    0.04 |    2 | 0.0353 |     594 B |        2.25 |
|                                 |                  |              |           |          |          |       |         |      |        |           |             |
| **Custom_Send_Object_Polymorphic**  | **10**               | **Hotset80_20**  |  **32.09 ns** | **0.242 ns** | **0.226 ns** |  **1.00** |    **0.01** |    **1** | **0.0157** |     **264 B** |        **1.00** |
| MediatR_Send_Object_Polymorphic | 10               | Hotset80_20  |  72.84 ns | 0.385 ns | 0.360 ns |  2.27 |    0.02 |    2 | 0.0328 |     549 B |        2.08 |
|                                 |                  |              |           |          |          |       |         |      |        |           |             |
| **Custom_Send_Object_Polymorphic**  | **50**               | **Uniform**      |  **81.04 ns** | **0.602 ns** | **0.470 ns** |  **1.00** |    **0.01** |    **1** | **0.0157** |     **264 B** |        **1.00** |
| MediatR_Send_Object_Polymorphic | 50               | Uniform      | 203.43 ns | 0.854 ns | 0.757 ns |  2.51 |    0.02 |    2 | 0.0358 |     599 B |        2.27 |
|                                 |                  |              |           |          |          |       |         |      |        |           |             |
| **Custom_Send_Object_Polymorphic**  | **50**               | **Hotset80_20**  |  **55.02 ns** | **0.561 ns** | **0.525 ns** |  **1.00** |    **0.01** |    **1** | **0.0157** |     **264 B** |        **1.00** |
| MediatR_Send_Object_Polymorphic | 50               | Hotset80_20  | 124.86 ns | 1.281 ns | 1.000 ns |  2.27 |    0.03 |    2 | 0.0350 |     590 B |        2.23 |
|                                 |                  |              |           |          |          |       |         |      |        |           |             |
| **Custom_Send_Object_Polymorphic**  | **100**              | **Uniform**      |  **99.68 ns** | **0.397 ns** | **0.310 ns** |  **1.00** |    **0.00** |    **1** | **0.0157** |     **264 B** |        **1.00** |
| MediatR_Send_Object_Polymorphic | 100              | Uniform      | 238.44 ns | 2.714 ns | 2.119 ns |  2.39 |    0.02 |    2 | 0.0358 |     599 B |        2.27 |
|                                 |                  |              |           |          |          |       |         |      |        |           |             |
| **Custom_Send_Object_Polymorphic**  | **100**              | **Hotset80_20**  |  **73.28 ns** | **1.463 ns** | **1.502 ns** |  **1.00** |    **0.03** |    **1** | **0.0157** |     **264 B** |        **1.00** |
| MediatR_Send_Object_Polymorphic | 100              | Hotset80_20  | 153.17 ns | 1.464 ns | 2.146 ns |  2.09 |    0.05 |    2 | 0.0355 |     595 B |        2.25 |
