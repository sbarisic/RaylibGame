# WaveFunctionCollapse library

This project vendors the algorithmic `Model.cs` core from
[mxgmn/WaveFunctionCollapse](https://github.com/mxgmn/WaveFunctionCollapse) at
commit `de7d22e705e816b62b4d613199d0463820fcaef3`.

The upstream MIT license is preserved in `LICENSE`. The application-specific
image, XML, sample, and command-line code is intentionally excluded.

Local changes are limited to a namespace/public library surface, an initial
constraint hook, cancellation and work-budget checks, contradiction reporting,
and a generic simple-tiled adapter. The observation, weighted entropy selection,
AC-4 compatible-count propagation, and ban stack follow the upstream model.
