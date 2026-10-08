/*
 * Executable File IO tour and checks
 * ---------------------------------
 * What: an ordered walkthrough of serialization, CSV, stream corner cases, and
 * file-lock recovery. Each successful operation and expected failure is checked.
 * Shows: how to call the other FileIO classes and how to recognize exceptions
 * without silently swallowing unexpected failures.
 * How: RunAsync creates an isolated temporary directory, runs four topic groups,
 * and deletes the directory in finally after their handles have been disposed.
 * Check throws for wrong results; Throws/ThrowsAsync require a specific exception.
 * ShortReadStream deliberately returns tiny reads to make a subtle bug repeatable.
 * Use: run `dotnet run` from the project root (Program already invokes this tour),
 * or call `await FileIOTour.RunAsync();` from your own async entry point.
 * No external fixtures or packages are needed; sample files ship with the build.
 * A clean run means all executed checks passed.
 * Lock-dependent checks report and skip when this environment does not enforce
 * exclusive sharing; Windows-specific behavior still needs testing on Windows.
 * Sample files: FileIO/Examples (see its README for expected results).
 * Run `dotnet run` to check the shipped samples automatically. For manual
 * reads, resolve paths from the repo root or AppContext.BaseDirectory; save
 * outputs in a temporary directory to keep these fixtures reusable.
 */

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace InterviewToolkit.FileIO;

/// <summary>Executable examples and regression checks; every file lives in an isolated temporary directory.</summary>
public static class FileIOTour
{
    /// <summary>Run every topic, print the outcomes, and clean up isolated sample files.</summary>
    public static async Task RunAsync()
    {
        Console.WriteLine("=== File IO tour ===");
        string directory = Directory.CreateTempSubdirectory("interview-fileio-").FullName;
        try
        {
            await SampleFilesAsync(directory);
            await SerializationAsync(directory);
            Csv(directory);
            await StreamsAsync(directory);
            await LockingAsync(directory);
            Console.WriteLine("  All File IO checks passed.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // Read repository fixtures from the build output, and write round-trip results
    // only into our temporary workspace. Malformed samples are expected failures.
    private static async Task SampleFilesAsync(string directory)
    {
        string examples = Path.Combine(AppContext.BaseDirectory, "FileIO", "Examples");
        string Sample(string name) => Path.Combine(examples, name);
        Console.WriteLine("  Checking shipped FileIO/Examples samples:");
        var json = await JsonFiles.LoadAsync<Candidate>(Sample("candidate.json"));
        var xml = XmlFiles.Load(Sample("candidate.xml"));
        Check(json.Name == "Zoë & <friends>" && json.Score == 98.75m &&
            json.Skills.SequenceEqual(new[] { "C#", "IO" }), "sample JSON model");
        Check(xml.Name == json.Name && xml.Score == json.Score && xml.Skills.SequenceEqual(json.Skills),
            "sample XML matches JSON");
        Check((await JsonFiles.ReadSkillsAsync(Sample("candidate.json"))).SequenceEqual(json.Skills)
            && XmlFiles.ReadSkills(Sample("candidate.xml")).SequenceEqual(json.Skills), "sample document queries");
        foreach (string name in new[] { "null.json", "unknown-member.json", "malformed.json" })
            await ThrowsAsync<JsonException>(() => JsonFiles.LoadAsync<Candidate>(Sample(name)), name);
        Throws<InvalidOperationException>(() => XmlFiles.Load(Sample("malformed.xml")), "malformed.xml");
        Throws<XmlException>(() => XmlFiles.ReadSkills(Sample("dtd.xml")), "dtd.xml");
        var totals = CsvFiles.TotalByProduct(Sample("sales.csv"));
        Check(totals.Count == 3 && totals["Coffee, dark"] == 14.75m && totals["Mug \"blue\"\r\nlarge"] == 7m
            && totals[""] == 0m, "sample sales totals");
        var rows = CsvFiles.Read(Sample("edge-cases.csv")).ToArray();
        Check(rows.Length == 2 && rows[0].SequenceEqual(new[] { "plain", "comma, inside", "quote \"inside\"", "" })
            && rows[1].SequenceEqual(new[] { "two\nlines", "Unicode Zoë", "", "last" }), "sample CSV boundaries");
        Throws<FormatException>(() => CsvFiles.ReadSales(Sample("invalid-amount.csv")).ToArray(), "invalid-amount.csv");
        Throws<FormatException>(() => CsvFiles.Read(Sample("unterminated.csv")).ToArray(), "unterminated.csv");
        Check(FileUtilities.FindLines(Sample("notes.txt"), "needle").Select(hit => hit.Number)
            .SequenceEqual(new[] { 2, 3 }), "sample text search");
        Throws<DecoderFallbackException>(() => File.ReadAllText(Sample("invalid-utf8.dat"), FileUtilities.StrictUtf8),
            "invalid-utf8.dat");

        string jsonOutput = Path.Combine(directory, "sample-roundtrip.json");
        string xmlOutput = Path.Combine(directory, "sample-roundtrip.xml");
        string csvOutput = Path.Combine(directory, "sample-roundtrip.csv");
        await JsonFiles.SaveAsync(jsonOutput, json);
        XmlFiles.Save(xmlOutput, xml);
        CsvFiles.Write(csvOutput, rows);
        Check((await JsonFiles.LoadAsync<Candidate>(jsonOutput)).Name == json.Name
            && XmlFiles.Load(xmlOutput).Name == xml.Name
            && CsvFiles.Read(csvOutput).Zip(rows).All(pair => pair.First.SequenceEqual(pair.Second))
            && CsvFiles.Read(csvOutput).Count() == rows.Length, "sample serialization/write round trips");
    }

    // Start with valid round trips, then change the files into invalid inputs
    // to contrast JSON exceptions with XML reader and serializer exceptions.
    private static async Task SerializationAsync(string directory)
    {
        var candidate = new Candidate { Name = "Zoë & <friends>", Score = 98.75m, Skills = ["C#", "IO"] };
        string json = Path.Combine(directory, "candidate.json");
        await JsonFiles.SaveAsync(json, candidate);
        var fromJson = await JsonFiles.LoadAsync<Candidate>(json);
        Check(fromJson.Name == candidate.Name && fromJson.Score == candidate.Score &&
            fromJson.Skills.SequenceEqual(candidate.Skills), "JSON typed round trip");
        Check((await JsonFiles.ReadSkillsAsync(json)).SequenceEqual(candidate.Skills), "JSON document parsing");
        await File.WriteAllTextAsync(json, "null");
        await ThrowsAsync<JsonException>(() => JsonFiles.LoadAsync<Candidate>(json), "JSON null rejected");
        await File.WriteAllTextAsync(json, "{\"unexpected\":true}");
        await ThrowsAsync<JsonException>(() => JsonFiles.LoadAsync<Candidate>(json), "unknown JSON property rejected");
        await File.WriteAllTextAsync(json, "{broken");
        await ThrowsAsync<JsonException>(() => JsonFiles.LoadAsync<Candidate>(json), "malformed JSON rejected");

        string xml = Path.Combine(directory, "candidate.xml");
        XmlFiles.Save(xml, candidate);
        var fromXml = XmlFiles.Load(xml);
        Check(fromXml.Name == candidate.Name && fromXml.Score == candidate.Score &&
            fromXml.Skills.SequenceEqual(candidate.Skills), "XML typed round trip and escaped characters");
        Check(XmlFiles.ReadSkills(xml).SequenceEqual(candidate.Skills), "LINQ to XML parsing");
        File.WriteAllText(xml, "<!DOCTYPE candidate [<!ENTITY x 'expanded'>]><candidate><Name>&x;</Name></candidate>");
        Throws<XmlException>(() => XmlFiles.ReadSkills(xml), "DTD rejected");
        File.WriteAllText(xml, "<candidate>");
        Throws<InvalidOperationException>(() => XmlFiles.Load(xml), "XmlSerializer wraps malformed XML errors");
    }

    // Include cases that line-by-line splitting cannot handle, plus EOF states
    // that frequently cause parsers to lose a final field or add a phantom record.
    private static void Csv(string directory)
    {
        string path = Path.Combine(directory, "sales.csv");
        string[][] rows = [["Product", "Amount"], ["Coffee, dark", "12.50"],
            ["Coffee, dark", "2.25"], ["Mug \"blue\"\r\nlarge", "7.00"], ["", "0"]];
        CsvFiles.Write(path, rows);
        Check(CsvFiles.Read(path).SelectMany(row => row).SequenceEqual(rows.SelectMany(row => row)),
            "CSV commas, escaped quotes, multiline and empty fields");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Check(CsvFiles.TotalByProduct(path)["Coffee, dark"] == 14.75m, "CSV streaming aggregation ignores current culture");
        }
        finally { CultureInfo.CurrentCulture = previous; }
        foreach (var (input, expected) in new (string, string[][])[]
        {
            ("", []), ("\n", [[""]]), ("a,b,", [["a", "b", ""]]),
            ("\"\"", [[""]]), ("a\rb\nc\r\n", [["a"], ["b"], ["c"]])
        })
        {
            using var reader = new StringReader(input);
            var actual = CsvFiles.Parse(reader).ToArray();
            Check(actual.Length == expected.Length && actual.Zip(expected).All(pair => pair.First.SequenceEqual(pair.Second)),
                "CSV EOF and newline boundaries");
        }
        foreach (string invalid in new[] { "\"unfinished", "a\"b", "\"a\"oops" })
            Throws<FormatException>(() => CsvFiles.Parse(new StringReader(invalid)).ToArray(), "malformed CSV rejected");
        File.WriteAllText(path, "Product,Amount\nMug,nope");
        Throws<FormatException>(() => CsvFiles.ReadSales(path).ToArray(), "CSV invalid numeric value rejected");
        Check(CsvFiles.ForSpreadsheet("=1+1") == "'=1+1", "spreadsheet formula mitigation is opt-in");
    }

    // Each check demonstrates an assumption about streams or paths that fails
    // in real applications, followed by the appropriate handling pattern.
    private static async Task StreamsAsync(string directory)
    {
        string path = Path.Combine(directory, "notes.txt");
        File.WriteAllText(path, "abcdefghij", FileUtilities.StrictUtf8);
        using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write))
            stream.Write(Encoding.UTF8.GetBytes("XY"));
        Check(File.ReadAllText(path) == "XYcdefghij", "OpenOrCreate does not truncate old content");
        await FileUtilities.ReplaceTextAsync(path, "short\nneedle\nlast");
        Check(File.ReadAllText(path) == "short\nneedle\nlast", "staged replacement removes old trailing bytes");
        Check(FileUtilities.FindLines(path, "needle").Single().Number == 2, "lazy line filtering");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => FileUtilities.ReplaceTextAsync(path, "lost", canceled.Token),
                "canceled replacement");
            Check(File.ReadAllText(path).StartsWith("short", StringComparison.Ordinal), "cancellation preserves destination");
            Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "staging files cleaned up");
        }
        using (var stream = new ShortReadStream([1, 2, 3]))
        {
            Check((await FileUtilities.ReadExactlyAsync(stream, 3)).SequenceEqual(new byte[] { 1, 2, 3 }), "short reads handled");
            await ThrowsAsync<EndOfStreamException>(() => FileUtilities.ReadExactlyAsync(stream, 1), "unexpected EOF");
        }
        using (var memory = new MemoryStream())
        {
            using (var writer = new StreamWriter(memory, FileUtilities.StrictUtf8, 1024, leaveOpen: true))
                writer.Write("Zoë"); // Dispose flushes buffered text but leaves the stream alive.
            Check(memory.Position == memory.Length, "writer flush and stream position");
            memory.Position = 0;
            using var reader = new StreamReader(memory, FileUtilities.StrictUtf8);
            Check(reader.ReadToEnd() == "Zoë", "rewind before reading");
        }
        File.WriteAllBytes(path, [0xc3, 0x28]);
        Throws<DecoderFallbackException>(() => File.ReadAllText(path, FileUtilities.StrictUtf8), "invalid UTF-8 rejected");
        Throws<ArgumentException>(() => FileUtilities.ResolveUnderDirectory(directory, "../escape"), "path traversal rejected");
        Throws<ArgumentException>(() => FileUtilities.ResolveUnderDirectory(directory, Path.GetFullPath(path)), "rooted path rejected");
        Check(FileUtilities.ResolveUnderDirectory(directory, "notes.txt") == path, "relative path resolution");
        // CreateNew does the existence check and creation together. File.Exists
        // followed by Create has a time-of-check/time-of-use race.
        Throws<IOException>(() => { using var file = new FileStream(path, FileMode.CreateNew); }, "CreateNew refuses overwrite");
    }

    // Hold a real handle, observe a competing-open failure, then release the
    // owner during retry. No background timing race is needed for this example.
    private static async Task LockingAsync(string directory)
    {
        string path = Path.Combine(directory, "locked.txt");
        File.WriteAllText(path, "locked");
        FileStream? holder = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            IOException? conflict = null;
            try { using var competing = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception) { conflict = exception; }
            if (conflict is null)
            {
                Console.WriteLine("  FileShare.None was not enforced here; filesystem/runtime locking varies.");
                return;
            }
            Console.WriteLine("  competing open failed; retrying after owner disposes its handle");
            // The classifier is scoped to an observed error on this known file.
            // It is not a general Unix sharing-error classifier.
            int code = conflict.HResult;
            int retries = 0;
            await using var recovered = await FileUtilities.OpenExclusiveAsync(path, attempts: 3,
                delay: TimeSpan.FromMilliseconds(10), isTransient: exception => exception.HResult == code,
                onRetry: _ => { retries++; holder.Dispose(); holder = null; });
            Check(retries == 1 && recovered.CanWrite, "bounded retry recovers after lock release");
        }
        finally { holder?.Dispose(); }
        using (var holderAgain = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await ThrowsAsync<IOException>(() => FileUtilities.OpenExclusiveAsync(path, attempts: 1), "retry exhaustion propagates error");
        await ThrowsAsync<FileNotFoundException>(() => FileUtilities.OpenExclusiveAsync(Path.Combine(directory, "missing")),
            "missing files are not retried as sharing failures");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException($"File IO check failed: {label}");
        Console.WriteLine($"  {label}: passed");
    }

    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Console.WriteLine($"  {label}: threw {typeof(T).Name}"); return; }
        throw new InvalidOperationException($"{label}: expected {typeof(T).Name}");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action, string label) where T : Exception
    {
        try { await action(); }
        catch (T) { Console.WriteLine($"  {label}: threw {typeof(T).Name}"); return; }
        throw new InvalidOperationException($"{label}: expected {typeof(T).Name}");
    }

    // Deliberately returns at most one byte to reproduce a legal partial read.
    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
