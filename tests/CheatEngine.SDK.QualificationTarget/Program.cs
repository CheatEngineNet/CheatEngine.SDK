using System.Globalization;

namespace QualificationTarget;

/// <summary>
///     Entry point. Arguments: <c>--heap-copies N</c> (1-1024, default 8), <c>--repetitions M</c> (1-1000000, default
///     20000) and <c>--ready-file PATH</c> (optional copy of the ready record). Standard input commands: <c>step</c>
///     advances the value cells, <c>exit</c> (or end of input) ends the process with exit code 0.
/// </summary>
internal static class Program
{
	private const int DefaultHeapCopies = 8;
	private const int DefaultRepetitions = 20_000;

	private static int Main(string[] args)
	{
		if (!TryParse(args, out int heapCopies, out int repetitions, out string? readyFile, out string? error))
		{
			Console.Error.WriteLine(error);
			return 2;
		}

		TargetMemory memory = new(heapCopies, repetitions);
		(nint imageBase, int imageSize) = ImageScanner.MainImage();
		int moduleMarkers = ImageScanner.CountInImage(imageBase, TargetLayout.Marker);
		string ready = ReadyRecord.Create(memory, imageBase, imageSize, moduleMarkers);
		Console.Out.WriteLine(ready);
		Console.Out.Flush();
		if (readyFile is not null)
		{
			File.WriteAllText(readyFile, ready + Environment.NewLine);
		}

		return RunCommands(memory);
	}

	private static int RunCommands(TargetMemory memory)
	{
		while (Console.In.ReadLine() is { } line)
		{
			switch (line.Trim())
			{
				case "exit":
					return 0;
				case "step":
					memory.Step();
					Console.Out.WriteLine(ReadyRecord.Step(memory));
					break;
				case "":
					break;
				default:
					Console.Out.WriteLine(ReadyRecord.Error("Unknown command; use step or exit."));
					break;
			}

			Console.Out.Flush();
		}

		// End of input: the runner closed the pipe.
		return 0;
	}

	private static bool TryParse(string[] args, out int heapCopies, out int repetitions, out string? readyFile,
		out string? error)
	{
		heapCopies = DefaultHeapCopies;
		repetitions = DefaultRepetitions;
		readyFile = null;
		error = null;
		for (int index = 0; index < args.Length; index++)
		{
			string? value = index + 1 < args.Length ? args[index + 1] : null;
			switch (args[index])
			{
				case "--heap-copies" when TryRange(value, 1, 1024, out heapCopies):
				case "--repetitions" when TryRange(value, 1, 1_000_000, out repetitions):
					index++;
					break;
				case "--ready-file" when !string.IsNullOrWhiteSpace(value):
					readyFile = value;
					index++;
					break;
				default:
					error = "Invalid argument '" + args[index] +
							"'. Use --heap-copies 1-1024, --repetitions 1-1000000 and --ready-file PATH.";
					return false;
			}
		}

		return true;
	}

	private static bool TryRange(string? text, int minimum, int maximum, out int value)
	{
		return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= minimum &&
			   value <= maximum;
	}
}
