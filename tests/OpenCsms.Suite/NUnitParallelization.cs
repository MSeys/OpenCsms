using NUnit.Framework;

// Every suite test owns its tenant, tariff and station and shares only the containers, the API and the
// worker; the tap's await predicates filter by test-owned ids, so tests can run concurrently. A test
// that cannot isolate its state leaves this policy and records why here.
[assembly: LevelOfParallelism(8)]
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
