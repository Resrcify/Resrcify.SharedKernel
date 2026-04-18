```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
Unknown processor
.NET SDK 10.0.102
  [Host]     : .NET 10.0.2 (10.0.225.61305), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.2 (10.0.225.61305), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                             | Mean        | Error      | StdDev     | Ratio  | RatioSD | Rank | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |------------:|-----------:|-----------:|-------:|--------:|-----:|-------:|----------:|------------:|
| Custom_Send_Typed_Task             |    33.48 ns |   0.186 ns |   0.145 ns |   1.00 |    0.01 |    3 | 0.0043 |      72 B |        1.00 |
| Custom_Send_Typed_ValueTask        |    28.79 ns |   0.176 ns |   0.156 ns |   0.86 |    0.01 |    1 | 0.0043 |      72 B |        1.00 |
| MediatR_Send_Typed                 |    55.01 ns |   0.677 ns |   0.565 ns |   1.64 |    0.02 |    5 | 0.0263 |     440 B |        6.11 |
| Custom_Send_Object_Task            |    37.35 ns |   0.253 ns |   0.225 ns |   1.12 |    0.01 |    4 | 0.0100 |     168 B |        2.33 |
| Custom_Send_Object_ValueTask       |    31.49 ns |   0.557 ns |   0.521 ns |   0.94 |    0.02 |    2 | 0.0057 |      96 B |        1.33 |
| MediatR_Send_Object                |    62.13 ns |   0.579 ns |   0.513 ns |   1.86 |    0.02 |    6 | 0.0319 |     536 B |        7.44 |
| Custom_Send_With_PrePost_Task      |    38.34 ns |   0.333 ns |   0.278 ns |   1.15 |    0.01 |    4 | 0.0086 |     144 B |        2.00 |
| Custom_Send_With_PrePost_ValueTask |    29.79 ns |   0.317 ns |   0.296 ns |   0.89 |    0.01 |    1 | 0.0043 |      72 B |        1.00 |
| MediatR_Send_With_PrePost          |    55.47 ns |   0.274 ns |   0.229 ns |   1.66 |    0.01 |    5 | 0.0263 |     440 B |        6.11 |
| Custom_Publish                     |    28.26 ns |   0.219 ns |   0.183 ns |   0.84 |    0.01 |    1 | 0.0019 |      32 B |        0.44 |
| MediatR_Publish                    |    65.07 ns |   0.581 ns |   0.543 ns |   1.94 |    0.02 |    7 | 0.0353 |     592 B |        8.22 |
| Custom_Stream_ConsumeAll           | 5,521.45 ns | 108.053 ns | 101.073 ns | 164.94 |    3.00 |    8 | 0.0305 |     560 B |        7.78 |
| MediatR_Stream_ConsumeAll          | 7,623.08 ns | 105.045 ns |  98.259 ns | 227.71 |    3.00 |    9 | 0.0763 |    1410 B |       19.58 |
