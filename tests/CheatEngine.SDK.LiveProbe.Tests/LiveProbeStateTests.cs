using LiveProbe;

namespace CheatEngine.SDK.LiveProbe.Tests;

public sealed class LiveProbeStateTests
{
	[Fact]
	public void CaptureHostProfile_when_authorization_expires_after_enable_returns_fresh_denial()
	{
		int evaluationCount = 0;
		AuthorizationDecision evaluateAuthorization()
		{
			evaluationCount++;
			return evaluationCount == 1
				? AllowedAuthorization()
				: AuthorizationDecision.Denied("The authorization manifest has expired.");
		}
		LiveProbeState.ValidateAfterEnable(evaluateAuthorization, static () => 401);
		bool wasCaptured = false;

		string result = LiveProbeState.CaptureHostProfile(
			evaluateAuthorization,
			static () => throw new InvalidOperationException("A denied authorization must not read CE's PID."),
			_ =>
			{
				wasCaptured = true;
				return "captured";
			});

		Assert.Equal("Live probe denied: The authorization manifest has expired.", result);
		Assert.False(wasCaptured);
		Assert.Equal(2, evaluationCount);
	}

	[Fact]
	public void CaptureHostProfile_when_target_image_changes_after_enable_returns_fresh_denial()
	{
		int evaluationCount = 0;
		AuthorizationDecision evaluateAuthorization()
		{
			evaluationCount++;
			return evaluationCount == 1
				? AllowedAuthorization()
				: AuthorizationDecision.Denied("The declared target SHA-256 differs from its live process image.");
		}
		LiveProbeState.ValidateAfterEnable(evaluateAuthorization, static () => 401);
		bool wasCaptured = false;

		string result = LiveProbeState.CaptureHostProfile(
			evaluateAuthorization,
			static () => throw new InvalidOperationException("A denied authorization must not read CE's PID."),
			_ =>
			{
				wasCaptured = true;
				return "captured";
			});

		Assert.Equal("Live probe denied: The declared target SHA-256 differs from its live process image.", result);
		Assert.False(wasCaptured);
		Assert.Equal(2, evaluationCount);
	}

	[Fact]
	public void CaptureHostProfile_when_ce_target_pid_changes_after_enable_returns_denial()
	{
		int evaluationCount = 0;
		AuthorizationDecision evaluateAuthorization()
		{
			evaluationCount++;
			return AllowedAuthorization();
		}
		LiveProbeState.ValidateAfterEnable(evaluateAuthorization, static () => 401);
		bool wasCaptured = false;

		string result = LiveProbeState.CaptureHostProfile(evaluateAuthorization, static () => 402,
			_ =>
			{
				wasCaptured = true;
				return "captured";
			});

		Assert.Equal("Live probe denied: CE reports opened process 402, not manifest process 401.", result);
		Assert.False(wasCaptured);
		Assert.Equal(2, evaluationCount);
		Assert.Contains("Runtime gate: allowed; CE's opened process matches the disposable-target manifest.",
			LiveProbeState.GetStatus());
	}

	[Fact]
	public void CaptureHostProfile_when_fresh_authorization_and_pid_match_passes_fresh_decision_to_capture()
	{
		int evaluationCount = 0;
		AuthorizationDecision evaluateAuthorization()
		{
			evaluationCount++;
			return evaluationCount == 1
				? AuthorizationDecision.Denied("The manifest was not present during enable.")
				: AllowedAuthorization();
		}
		LiveProbeState.ValidateAfterEnable(evaluateAuthorization,
			static () => throw new InvalidOperationException("The denied enable state must not read CE's PID."));

		string result = LiveProbeState.CaptureHostProfile(evaluateAuthorization, static () => 401,
			static authorization => authorization.TargetPath);

		Assert.Equal("C:\\disposable-target.exe", result);
		Assert.Equal(2, evaluationCount);
		Assert.Contains("Runtime gate: denied; The manifest was not present during enable.",
			LiveProbeState.GetStatus());
	}

	[Fact]
	public void TryRequireRuntimeAuthorization_when_manifest_expires_after_enable_rechecks_and_denies()
	{
		int evaluationCount = 0;
		AuthorizationDecision evaluateAuthorization()
		{
			evaluationCount++;
			return evaluationCount == 1
				? AllowedAuthorization()
				: AuthorizationDecision.Denied("The authorization manifest has expired.");
		}
		LiveProbeState.ValidateAfterEnable(evaluateAuthorization, static () => 401);

		bool isAllowed = LiveProbeState.TryRequireRuntimeAuthorization(evaluateAuthorization,
			static () => throw new InvalidOperationException("A denied authorization must not read CE's PID."),
			out string denial);

		Assert.False(isAllowed);
		Assert.Equal("Live probe denied: The authorization manifest has expired.", denial);
		Assert.Equal(2, evaluationCount);
		Assert.Contains("Runtime gate: allowed; CE's opened process matches the disposable-target manifest.",
			LiveProbeState.GetStatus());
	}

	[Fact]
	public void TryRequireRuntimeAuthorization_when_ce_target_pid_changes_after_enable_rechecks_and_denies()
	{
		int evaluationCount = 0;
		AuthorizationDecision evaluateAuthorization()
		{
			evaluationCount++;
			return AllowedAuthorization();
		}
		LiveProbeState.ValidateAfterEnable(evaluateAuthorization, static () => 401);

		bool isAllowed = LiveProbeState.TryRequireRuntimeAuthorization(evaluateAuthorization, static () => 402,
			out string denial);

		Assert.False(isAllowed);
		Assert.Equal("Live probe denied: CE reports opened process 402, not manifest process 401.", denial);
		Assert.Equal(2, evaluationCount);
		Assert.Contains("Runtime gate: allowed; CE's opened process matches the disposable-target manifest.",
			LiveProbeState.GetStatus());
	}

	private static AuthorizationDecision AllowedAuthorization()
	{
		return AuthorizationDecision.Allowed("C:\\ce.exe", "HOST", 401, "C:\\disposable-target.exe", "TARGET",
			DateTimeOffset.MaxValue);
	}
}
