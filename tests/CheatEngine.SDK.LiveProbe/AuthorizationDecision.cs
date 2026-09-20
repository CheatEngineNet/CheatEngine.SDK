namespace LiveProbe;

internal readonly record struct AuthorizationDecision(bool IsAllowed, string Reason, string HostPath, string HostSha256,
    int TargetProcessId, string TargetPath, string TargetSha256, DateTimeOffset ExpiresUtc)
{
    internal static AuthorizationDecision Denied(string reason)
    {
        return new AuthorizationDecision(false, reason, string.Empty, string.Empty, 0, string.Empty, string.Empty, default);
    }

    internal static AuthorizationDecision Allowed(string hostPath, string hostSha256, int targetProcessId, string targetPath,
        string targetSha256, DateTimeOffset expiresUtc)
    {
        return new AuthorizationDecision(true, "Host and disposable target manifest are verified.", hostPath, hostSha256,
            targetProcessId, targetPath, targetSha256, expiresUtc);
    }
}
