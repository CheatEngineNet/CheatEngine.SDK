namespace LiveProbe;

/// <summary>One evaluation of the fault-injection switch.</summary>
/// <param name="Stage">The stage that throws during this enable; <see cref="LiveProbeFaultStage.None" /> when ignored.</param>
/// <param name="Reason">Why this stage was selected or why the switch was ignored, for the log and the status record.</param>
/// <param name="FileFound">Whether a switch file existed next to the plugin and was read.</param>
internal readonly record struct LiveProbeFaultDecision(LiveProbeFaultStage Stage, string Reason, bool FileFound)
{
	internal static LiveProbeFaultDecision NotEvaluated =>
		new(LiveProbeFaultStage.None, "Not evaluated: no enable has run.", false);
}
