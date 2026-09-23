namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>A ci.yml job of shared contract 1.6. Runner and timeout are null for a reusable-workflow call.</summary>
internal sealed record PipelineJob(string Id, string Name, string? RunsOn, int? TimeoutMinutes);
