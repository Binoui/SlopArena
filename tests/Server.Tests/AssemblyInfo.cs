using Xunit;

// ponytail: ArenaRegistry is process-global and HttpListener tests reserve transient
// host ports; keep this small suite serial until fixtures own isolated resources.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
