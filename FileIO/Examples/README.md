# Sample files for the File IO tour

From the project root, run:

```sh
dotnet run
```

`FileIOTour` automatically reads every sample below and checks its expected
result, including the intended exceptions. It writes round-trip output into an
isolated temporary directory and deletes that directory afterward. The source
samples remain unchanged. The project copies this folder into build and publish
output; the tour resolves it relative to `AppContext.BaseDirectory`.

## Sample inventory

| File | Use with | Expected result / lesson |
| --- | --- | --- |
| [candidate.json](candidate.json) | `JsonFiles.LoadAsync<Candidate>`, `ReadSkillsAsync` | Name `Zoë & <friends>`, score `98.75`, skills `C#`, `IO` |
| [candidate.xml](candidate.xml) | `XmlFiles.Load`, `ReadSkills` | Same model as JSON; escaped XML text becomes ordinary characters |
| [null.json](null.json) | `JsonFiles.LoadAsync<Candidate>` | `JsonException`: top-level null rejected |
| [unknown-member.json](unknown-member.json) | `JsonFiles.LoadAsync<Candidate>` | `JsonException`: unexpected property rejected |
| [malformed.json](malformed.json) | `JsonFiles.LoadAsync<Candidate>` | `JsonException`: invalid JSON syntax |
| [malformed.xml](malformed.xml) | `XmlFiles.Load` | `InvalidOperationException` wrapping the XML parse error |
| [dtd.xml](dtd.xml) | `XmlFiles.ReadSkills` | `XmlException`: DTD prohibited; contains only an internal entity, no external reference |
| [sales.csv](sales.csv) | `CsvFiles.ReadSales`, `TotalByProduct` | Four sales; three product groups; quoted comma, doubled quotes, embedded CRLF, empty product |
| [edge-cases.csv](edge-cases.csv) | `CsvFiles.Read` | Two four-field records; trailing empty field, quoted LF, Unicode, empty quoted field, no final newline |
| [invalid-amount.csv](invalid-amount.csv) | `CsvFiles.ReadSales` | `FormatException` on logical record 2: amount is not numeric |
| [unterminated.csv](unterminated.csv) | `CsvFiles.Read` | `FormatException`: quoted field never closes |
| [notes.txt](notes.txt) | `FileUtilities.FindLines(path, "needle")` | Matches lines 2 and 3 |
| [invalid-utf8.dat](invalid-utf8.dat) | `File.ReadAllText(path, FileUtilities.StrictUtf8)` | `DecoderFallbackException`: bytes `C3 28` are invalid UTF-8 |

Sales totals are `Coffee, dark = 14.75`, `Mug "blue"\r\nlarge = 7.00`, and the
empty product name `= 0`. CSV records can span physical lines, so an editor's line
numbers differ from the record numbers reported by `ReadSales`.

## Calling the classes yourself

In an async method, use output-relative paths to work regardless of the process's
current directory:

```csharp
using InterviewToolkit.FileIO;

string examples = Path.Combine(AppContext.BaseDirectory, "FileIO", "Examples");
string Sample(string name) => Path.Combine(examples, name);

var candidate = await JsonFiles.LoadAsync<Candidate>(Sample("candidate.json"));
var xmlCandidate = XmlFiles.Load(Sample("candidate.xml"));
var totals = CsvFiles.TotalByProduct(Sample("sales.csv"));
foreach (var row in CsvFiles.Read(Sample("edge-cases.csv")))
    Console.WriteLine(string.Join(" | ", row));
foreach (var hit in FileUtilities.FindLines(Sample("notes.txt"), "needle"))
    Console.WriteLine($"Line {hit.Number}: {hit.Text}");
```

If running from the repository root, `Path.Combine("FileIO", "Examples")` also
works. The folder is not a command-line input argument; `dotnet run` already
invokes the sample checks. To experiment with a malformed sample, put the awaited
load or the entire CSV enumeration inside a try/catch for the expected exception.
Creating a lazy sequence alone does not execute its parser.

For write experiments, load a sample and save to a new temporary destination:

```csharp
string output = Directory.CreateTempSubdirectory("fileio-practice-").FullName;
try
{
    await JsonFiles.SaveAsync(Path.Combine(output, "copy.json"), candidate);
    XmlFiles.Save(Path.Combine(output, "copy.xml"), xmlCandidate);
    CsvFiles.Write(Path.Combine(output, "copy.csv"), CsvFiles.Read(Sample("edge-cases.csv")));
    await FileUtilities.ReplaceTextAsync(Path.Combine(output, "notes.txt"), "replacement\n");
    // Reload the copies here and compare their values, not formatting or byte order.
}
finally
{
    Directory.Delete(output, recursive: true);
}
```

Do not write back to an input being lazily enumerated: the writer can truncate it
before the iterator opens it. Also avoid saving over these fixtures unless you
intend to change the expected checks. CRLF inside the quoted field in `sales.csv`
and the missing final newlines are deliberate byte-level cases; editor newline
normalization can change what those examples test. `invalid-utf8.dat` is binary
and should not be resaved by a text editor. Lock recovery still uses a generated
working file so that locking tests cannot damage these samples.
