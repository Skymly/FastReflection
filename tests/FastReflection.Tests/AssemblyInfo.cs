// FallbackTests toggles a global switch (EmitHelper.ForceReflectionFallback);
// parallel test collections would race on it.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
