using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>
///     A complete Cheat Engine file version, represented without the lossy floating-point form returned by
///     <c>getCEVersion</c>.
/// </summary>
/// <remarks>
///     CE 7.7 exposes the complete <c>major</c>, <c>minor</c>, <c>release</c> and <c>build</c> fields through
///     <c>getCheatEngineFileVersion</c>, which the SDK reads with
///     <c>CheatEngine.SDK.Engine.Processes.RuntimeHostOperations.TryGetCheatEngineFileVersion</c> (a hand-written
///     binding: the call returns a packed integer and a table). This type must never be constructed by converting
///     <c>getCEVersion</c>'s floating-point result. It is immutable and does not query Cheat Engine.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct CheatEngineVersion : IEquatable<CheatEngineVersion>, IComparable<CheatEngineVersion>
{
	/// <summary>The CE 7.7.0.10621 build that defines this SDK's current compatibility reference.</summary>
	public static CheatEngineVersion Ce77010621 => new(7, 7, 0, 10621);

	/// <summary>Initializes a complete Cheat Engine file version.</summary>
	/// <param name="major">The non-negative major component.</param>
	/// <param name="minor">The non-negative minor component.</param>
	/// <param name="release">The non-negative release component.</param>
	/// <param name="build">The non-negative build component.</param>
	/// <exception cref="System.ArgumentOutOfRangeException">At least one component is negative.</exception>
	public CheatEngineVersion(int major, int minor, int release, int build)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(major);
		ArgumentOutOfRangeException.ThrowIfNegative(minor);
		ArgumentOutOfRangeException.ThrowIfNegative(release);
		ArgumentOutOfRangeException.ThrowIfNegative(build);
		Major = major;
		Minor = minor;
		Release = release;
		Build = build;
	}

	/// <summary>Gets the major file-version component.</summary>
	public int Major
	{
		get;
	}

	/// <summary>Gets the minor file-version component.</summary>
	public int Minor
	{
		get;
	}

	/// <summary>Gets the release file-version component.</summary>
	public int Release
	{
		get;
	}

	/// <summary>Gets the build file-version component.</summary>
	public int Build
	{
		get;
	}

	/// <summary>Compares two complete file versions component by component.</summary>
	/// <param name="other">The version to compare with this value.</param>
	/// <returns>
	///     A negative value, zero, or a positive value when this version is older than, equal to, or newer than
	///     <paramref name="other" />.
	/// </returns>
	public int CompareTo(CheatEngineVersion other)
	{
		int result = Major.CompareTo(other.Major);
		if (result != 0)
		{
			return result;
		}

		result = Minor.CompareTo(other.Minor);
		if (result != 0)
		{
			return result;
		}

		result = Release.CompareTo(other.Release);
		return result != 0 ? result : Build.CompareTo(other.Build);
	}

	/// <inheritdoc />
	public bool Equals(CheatEngineVersion other)
	{
		return Major == other.Major && Minor == other.Minor && Release == other.Release && Build == other.Build;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is CheatEngineVersion other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(Major, Minor, Release, Build);
	}

	/// <summary>Formats all four file-version components using invariant decimal digits.</summary>
	/// <returns>The <c>major.minor.release.build</c> representation.</returns>
	public override string ToString()
	{
		return Major.ToString(CultureInfo.InvariantCulture) + "." +
		       Minor.ToString(CultureInfo.InvariantCulture) + "." +
		       Release.ToString(CultureInfo.InvariantCulture) + "." +
		       Build.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>Tests two versions for equality.</summary>
	/// <param name="left">The first version.</param>
	/// <param name="right">The second version.</param>
	/// <returns><see langword="true" /> when every component is equal.</returns>
	public static bool operator ==(CheatEngineVersion left, CheatEngineVersion right)
	{
		return left.Equals(right);
	}

	/// <summary>Tests two versions for inequality.</summary>
	/// <param name="left">The first version.</param>
	/// <param name="right">The second version.</param>
	/// <returns><see langword="true" /> when at least one component differs.</returns>
	public static bool operator !=(CheatEngineVersion left, CheatEngineVersion right)
	{
		return !left.Equals(right);
	}

	/// <summary>Tests whether the first version is older than the second.</summary>
	/// <param name="left">The first version.</param>
	/// <param name="right">The second version.</param>
	/// <returns><see langword="true" /> when <paramref name="left" /> is older.</returns>
	public static bool operator <(CheatEngineVersion left, CheatEngineVersion right)
	{
		return left.CompareTo(right) < 0;
	}

	/// <summary>Tests whether the first version is newer than the second.</summary>
	/// <param name="left">The first version.</param>
	/// <param name="right">The second version.</param>
	/// <returns><see langword="true" /> when <paramref name="left" /> is newer.</returns>
	public static bool operator >(CheatEngineVersion left, CheatEngineVersion right)
	{
		return left.CompareTo(right) > 0;
	}

	/// <summary>Tests whether the first version is not newer than the second.</summary>
	/// <param name="left">The first version.</param>
	/// <param name="right">The second version.</param>
	/// <returns><see langword="true" /> when <paramref name="left" /> is equal to or older.</returns>
	public static bool operator <=(CheatEngineVersion left, CheatEngineVersion right)
	{
		return left.CompareTo(right) <= 0;
	}

	/// <summary>Tests whether the first version is not older than the second.</summary>
	/// <param name="left">The first version.</param>
	/// <param name="right">The second version.</param>
	/// <returns><see langword="true" /> when <paramref name="left" /> is equal to or newer.</returns>
	public static bool operator >=(CheatEngineVersion left, CheatEngineVersion right)
	{
		return left.CompareTo(right) >= 0;
	}
}
