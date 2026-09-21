using Xunit.Sdk;
using Xunit.v3;

// LiveProbeState preserves the enable-time diagnostic snapshot in process-wide static fields. Sequential execution
// keeps the injected authorization and PID sequences isolated without involving a Cheat Engine host.
[assembly: Parallelization(Mode = ParallelMode.None)]
