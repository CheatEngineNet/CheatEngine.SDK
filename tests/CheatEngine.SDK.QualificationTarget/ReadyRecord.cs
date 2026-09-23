using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace QualificationTarget;

/// <summary>
///     The one-line JSON records the target prints: the ready record (schema <see cref="Schema" />) on start, and one
///     step record per <c>step</c> command. Written with <see cref="Utf8JsonWriter" />, so nothing depends on
///     reflection and the Native AOT build stays trim-safe.
/// </summary>
internal static class ReadyRecord
{
	internal const string Schema = "cheatengine-qualification-target/v0";

	internal static string Create(TargetMemory memory, nint imageBase, int imageSize, int moduleMarkerCount)
	{
		string marker = TargetLayout.ToPattern(TargetLayout.Marker);
		return Write(writer =>
		{
			writer.WriteString("schema", Schema);
			writer.WriteNumber("pid", Environment.ProcessId);
			writer.WriteString("arch", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
			writer.WriteNumber("pointerSize", IntPtr.Size);
			writer.WriteString("imageBase", Hex(imageBase));
			writer.WriteNumber("imageSize", imageSize);
			writer.WriteStartArray("regions");
			Region(writer, "module-marker", imageBase, imageSize, marker, moduleMarkerCount);
			Region(writer, "heap-marker", memory.HeapMarkersAddress, memory.HeapMarkers.Length, marker,
				ImageScanner.Count(memory.HeapMarkers, TargetLayout.Marker));
			Region(writer, "many-results", memory.RepeatedAddress, memory.Repeated.Length,
				TargetLayout.ToPattern(memory.RepeatedPattern),
				ImageScanner.Count(memory.Repeated, memory.RepeatedPattern));
			writer.WriteEndArray();
			writer.WriteStartArray("values");
			Value(writer, "int32", memory.Int32Address, memory.Int32);
			Value(writer, "int64", memory.Int64Address, memory.Int64);
			writer.WriteEndArray();
		});
	}

	internal static string Step(TargetMemory memory)
	{
		return Write(writer =>
		{
			writer.WriteString("event", "step");
			writer.WriteNumber("int32", memory.Int32);
			writer.WriteNumber("int64", memory.Int64);
		});
	}

	internal static string Error(string message)
	{
		return Write(writer =>
		{
			writer.WriteString("event", "error");
			writer.WriteString("message", message);
		});
	}

	private static void Region(Utf8JsonWriter writer, string id, nint address, int length, string pattern, int count)
	{
		writer.WriteStartObject();
		writer.WriteString("id", id);
		writer.WriteString("base", Hex(address));
		writer.WriteNumber("length", length);
		writer.WriteString("pattern", pattern);
		writer.WriteNumber("count", count);
		writer.WriteEndObject();
	}

	private static void Value(Utf8JsonWriter writer, string id, nint address, long value)
	{
		writer.WriteStartObject();
		writer.WriteString("id", id);
		writer.WriteString("address", Hex(address));
		writer.WriteNumber("value", value);
		writer.WriteEndObject();
	}

	private static string Hex(nint address)
	{
		return "0x" + ((nuint) address).ToString("X", CultureInfo.InvariantCulture);
	}

	private static string Write(Action<Utf8JsonWriter> body)
	{
		using MemoryStream stream = new();
		using (Utf8JsonWriter writer = new(stream))
		{
			writer.WriteStartObject();
			body(writer);
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}
}
