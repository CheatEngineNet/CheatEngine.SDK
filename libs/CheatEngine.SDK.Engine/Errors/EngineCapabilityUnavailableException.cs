using System;

namespace CheatEngine.SDK.Engine.Errors;

/// <summary>
///     Represents a required Engine capability that is absent from the attached Cheat Engine runtime.
/// </summary>
/// <remarks>
///     This is selected before a binding is invoked, for an optional host subsystem or capability unavailable for the
///     attached Cheat Engine version or architecture. A required binding global that is absent or non-callable is
///     represented by <see cref="EngineGlobalUnavailableException" /> instead. The <see cref="Capability" /> is a stable
///     public capability identifier; bindings keep corresponding Lua global names internal.
/// </remarks>
public sealed class EngineCapabilityUnavailableException : EngineException
{
    /// <summary>Initializes an exception with the standard message for <paramref name="capability" />.</summary>
    /// <param name="capability">The stable public capability identifier.</param>
    public EngineCapabilityUnavailableException(string capability)
        : this(capability, CreateDefaultMessage(capability), innerException: null)
    {
    }

    /// <summary>Initializes an exception with a caller-supplied stable message.</summary>
    /// <param name="capability">The stable public capability identifier.</param>
    /// <param name="message">The stable public failure message.</param>
    public EngineCapabilityUnavailableException(string capability, string message)
        : this(capability, message, innerException: null)
    {
    }

    /// <summary>Initializes an exception with a stable message and the lower-level cause.</summary>
    /// <param name="capability">The stable public capability identifier.</param>
    /// <param name="message">The stable public failure message.</param>
    /// <param name="innerException">The lower-level cause, when one exists.</param>
    public EngineCapabilityUnavailableException(string capability, string message, Exception? innerException)
        : base(message, innerException)
    {
        Capability = RequireText(capability, nameof(capability));
    }

    /// <summary>Gets the stable public identifier of the unavailable capability.</summary>
    public string Capability { get; }

    /// <inheritdoc />
    public override EngineFailureKind Kind => EngineFailureKind.CapabilityUnavailable;

    private static string CreateDefaultMessage(string capability)
    {
        return "The Cheat Engine capability '" + RequireText(capability, nameof(capability)) + "' is unavailable.";
    }
}
