@{
	# The lint profile of every tracked *.ps1, applied by eng/ci/Invoke-ScriptAnalysis.ps1 in the CI lint job and locally.
	# Error and Warning records fail the job unless the script allowlists them for one file with a reason.
	Severity = @('Error', 'Warning')

	ExcludeRules = @(
		# CI scripts report through GitHub workflow commands (::error::, ::notice::), which are host output by design.
		'PSAvoidUsingWriteHost'
	)
}
