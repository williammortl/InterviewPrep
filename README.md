# InterviewPrep

A .NET 10 console project with explained data structures, algorithms, and file IO examples for interview preparation.

Run the executable tour and its checks:

```sh
dotnet run
```

See [the File IO guide](FileIO/README.md) for JSON/XML serialization, CSV parsing and writing, stream corner cases, and file-lock recovery.

## Testing with the included sample files

[FileIO/Examples](FileIO/Examples/README.md) contains valid JSON/XML candidates,
CSV sales and parsing boundary cases, searchable text, and deliberately malformed
inputs. Run `dotnet run` to validate them automatically as part of the File IO tour.
Expected parse failures count as successful checks; unexpected results throw.

The project copies these files into build/publish output. The tour reads them
using `Path.Combine(AppContext.BaseDirectory, "FileIO", "Examples")`, so it does
not depend on the working directory. Serialization and CSV write checks create
copies in a temporary directory, leaving the source samples untouched.

For manual use in an async method:

```csharp
string examples = Path.Combine(AppContext.BaseDirectory, "FileIO", "Examples");
var candidate = await JsonFiles.LoadAsync<Candidate>(Path.Combine(examples, "candidate.json"));
var totals = CsvFiles.TotalByProduct(Path.Combine(examples, "sales.csv"));
// totals["Coffee, dark"] is 14.75m.
```

The [sample README](FileIO/Examples/README.md) lists each fixture, its expected
values or exception, and read/write examples. CSV errors occur during enumeration.
Use a new output path for writes, especially when reading lazily from a sample.

# C# file IO and parsing: from first principles to reliable systems

This guide is intended to be read alongside the code, moving from everyday file
operations to the decisions that matter when files become large, shared, or
unreliable. The examples target this project's .NET 10 environment. Snippets
assume the relevant `System.IO`, `System.Text`, LINQ, and serialization namespaces;
project-specific examples also use `InterviewToolkit.FileIO`.

The runnable tour exercises the techniques implemented in `FileIO`. Sections on
binary formats, watchers, and advanced performance describe further study rather
than features already implemented here. The tour uses disposable temporary files,
prints check results, and throws when an unexpected result occurs.

## Reading map

1. [Files, bytes, text, and objects](#files-bytes-text-and-objects)
2. [Everyday file operations](#everyday-file-operations)
3. [Paths and directories](#paths-and-directories)
4. [Streams and resource ownership](#streams-and-resource-ownership)
5. [Encoding and text boundaries](#encoding-and-text-boundaries)
6. [Parsing and validation](#parsing-and-validation)
7. [JSON](#json)
8. [XML](#xml)
9. [CSV](#csv)
10. [Async IO and cancellation](#async-io-and-cancellation)
11. [Sharing, locking, and retry](#sharing-locking-and-retry)
12. [Replacement, transactions, and durability](#replacement-transactions-and-durability)
13. [Large files and advanced tools](#large-files-and-advanced-tools)
14. [Designing a dependable import](#designing-a-dependable-import)
15. [Interview questions and exercises](#interview-questions-and-exercises)

## Files, bytes, text, and objects

A file stores bytes. Text is an interpretation of those bytes through an encoding.
JSON, XML, and CSV give that text structure. Deserialization maps the structure to
C# values. Business validation decides whether those values are acceptable.

Think of an import as a sequence:

```text
path -> open handle -> bytes -> decoded text -> parsed structure
     -> typed values -> validated records -> application state
```

A failure at one stage has a different meaning from a failure at another. A file
may exist but be unreadable. It may be readable but contain invalid UTF-8. Its JSON
may be syntactically valid but use a string where a number is expected. Its typed
object may still contain an impossible score. Treating all those situations as
"bad file" throws away useful diagnostic information.

C# separates the layers into useful abstractions:

| Layer | Typical types | Responsibility |
| --- | --- | --- |
| Filesystem operations | `File`, `Directory`, `FileInfo`, `DirectoryInfo` | Open, create, enumerate, copy, move, inspect |
| Path manipulation | `Path` | Combine and normalize path strings |
| Bytes | `Stream`, `FileStream`, `MemoryStream` | Read/write byte sequences |
| Text | `TextReader`, `TextWriter`, `StreamReader`, `StreamWriter` | Decode/encode characters and buffer text |
| Structured data | `JsonSerializer`, `JsonDocument`, `XmlReader`, `XDocument` | Interpret a format |
| Application rules | Your models and validators | Decide what the data means |

The [Microsoft file and stream IO overview](https://learn.microsoft.com/en-us/dotnet/standard/io/)
provides the broader API map. In an interview, explaining which layer owns a
problem is often more useful than remembering every overload.

## Everyday file operations

For a small file, convenience methods are usually the clearest starting point:

```csharp
var utf8 = new UTF8Encoding(false, true);
File.WriteAllText("notes.txt", "hello\nworld", utf8);
string contents = File.ReadAllText("notes.txt", utf8);
File.AppendAllText("notes.txt", "\nthird line", utf8);
```

`WriteAllText` replaces the contents; `AppendAllText` writes at the end.
`ReadAllText` materializes the entire decoded file as a string. Their simplicity
is valuable, but the entire file must fit comfortably in memory when reading.
An async variant still returns a whole string and has the same fundamental
memory requirement.

For line-oriented work, distinguish these two choices:

```csharp
string[] allLines = File.ReadAllLines("notes.txt");
IEnumerable<string> lazyLines = File.ReadLines("notes.txt");
```

The first returns an array containing every line. The second produces lines as
you iterate. A lazy source can still become eager when you call `ToArray`,
`ToList`, `OrderBy`, or another operation that buffers data. Streaming behavior
is a property of the entire processing pipeline.

Use `FileMode`, `FileAccess`, and `FileShare` explicitly when the defaults matter:

| Mode | Missing file | Existing file | Common use |
| --- | --- | --- | --- |
| `CreateNew` | Create | Throw | Exclusive creation without an existence-check race |
| `Create` | Create | Truncate | Replace contents directly |
| `Open` | Throw | Open without truncation | Read or edit an existing file |
| `OpenOrCreate` | Create | Open without truncation | Create if absent, preserve if present |
| `Truncate` | Throw | Set length to zero | Clear an existing writable file |
| `Append` | Create | Position for appending | Write-only append operations |

`OpenOrCreate` is an especially common trap. Writing two bytes over a ten-byte
file leaves eight old bytes unless you also shorten the file. The tour checks
this behavior before showing staged replacement.

## Paths and directories

Relative paths resolve against the process's current working directory. That may
be the repository when you run from a terminal, but another directory under a
service, test runner, or IDE. `AppContext.BaseDirectory` identifies the application
base directory; it is useful for deployed assets, but may not be a suitable
writable location. Pick a storage directory deliberately.

```csharp
string root = Path.Combine(Path.GetTempPath(), "interview-notes");
Directory.CreateDirectory(root);
string path = Path.Combine(root, "notes.txt");
```

`Directory.CreateDirectory` also succeeds when the directory already exists.
`Path.Combine` avoids hardcoded separators, but does not establish containment:
a later rooted path can override the earlier root. Likewise, `..` can move upward.
`FileUtilities.ResolveUnderDirectory` normalizes a path and rejects lexical
escapes. It does not resolve symlinks or prevent an attacker from swapping a
directory after the check.

A path is a name, not a permanent identity. Between inspecting a name and opening
it, another process can replace the file. This is the time-of-check/time-of-use
problem. Prefer opening with the desired operation and handling its result over
checking `File.Exists` first. An existence check is useful for presentation, but
is not a reservation or proof that a later operation will succeed.

Filesystem case sensitivity, valid names, separator rules, and permissions vary
by operating system and filesystem. Avoid assuming that names differing only by
case identify different files everywhere. Do not trim or normalize user filenames
without understanding how that changes their meaning.

For a large directory, `Directory.EnumerateFiles` avoids creating an array of all
paths at once. Enumeration can encounter disappearing entries or permission
errors while it runs; handle those at the boundary where the application can
decide whether to skip, report, or stop.

## Streams and resource ownership

A stream represents bytes moving through an interface. The same parsing function
can work with a file, an in-memory buffer, or a network stream if it depends on
`Stream` or `TextReader` instead of opening paths itself. That separation also
makes failure cases easier to reproduce.

Every open file consumes an operating system handle. Dispose deterministically:

```csharp
using var stream = new FileStream(
    "notes.txt", FileMode.Open, FileAccess.Read, FileShare.Read);
using var reader = new StreamReader(stream);
Console.WriteLine(reader.ReadToEnd());
```

`using` is implemented with cleanup semantics similar to `try/finally`; it still
runs when an exception leaves the scope. Garbage collection does not promise
prompt release of file handles. A forgotten handle can look like a mysterious
file-locking bug much later.

Wrapper objects usually own their underlying streams unless told otherwise.
`leaveOpen: true` lets a writer or reader finish without closing a caller-owned
stream. The tour writes to a `MemoryStream`, disposes its text writer to flush,
rewinds, and reads the same bytes back.

A seekable stream has a position. Writing advances it. Reading at its end returns
EOF; it does not automatically rewind. Before setting `Position` or calling
`Seek`, check `CanSeek` if the stream's origin is unknown. Mixing a buffered text
reader with manual changes to the underlying stream position requires care:
the reader may already have buffered bytes and decoder state. Prefer one access
pattern or recreate the text reader at the intended position.

A byte read returns a count. It can return fewer bytes than requested while more
remain. Only process the returned portion:

```csharp
byte[] buffer = new byte[8192];
int count;
while ((count = await stream.ReadAsync(buffer.AsMemory())) != 0)
{
    ProcessBytes(buffer.AsSpan(0, count)); // Your application's byte consumer.
}
```

For a known-size field or header, use `ReadExactly`/`ReadExactlyAsync`, which loops
and throws `EndOfStreamException` if the stream ends early. The project's
`ShortReadStream` forces one-byte reads to demonstrate why one read is insufficient.

Ownership also applies to iterators. `CsvFiles.Read` holds its reader open while
its enumerator is active. `foreach` disposes the enumerator on normal completion,
`break`, or exception. A manually obtained enumerator needs its own `using`.

## Encoding and text boundaries

UTF-8 maps characters to variable-length byte sequences. A character can straddle
two byte reads. Decoding each chunk independently can therefore corrupt text;
use `StreamReader` or an `Encoding.GetDecoder()` instance that preserves state
between chunks.

`new UTF8Encoding(false, true)` means no BOM is emitted and invalid input is
rejected. This project's strict decoder throws instead of silently substituting
replacement characters. A BOM can identify an encoding, but does not guarantee
that the rest of a file is valid. The CSV reader enables BOM detection; writing
uses UTF-8 without one.

A .NET `string` is UTF-16. Its length counts UTF-16 code units, which need not
match Unicode scalar values or user-perceived characters. Byte offsets and string
indexes are different coordinate systems. If an import reports locations,
specify whether they are byte offsets, character positions, physical lines,
or logical records.

Line endings also affect parsing. Windows commonly uses CRLF, Unix commonly LF,
and input can contain CR alone. `ReadLine` removes terminators. That is convenient
for logs, but destructive when line breaks are part of a quoted CSV field. The
CSV parser reads characters and distinguishes record terminators from field data.

## Parsing and validation

Parsing recognizes structure. Conversion assigns types. Validation applies rules.
Keeping these stages understandable produces better errors and more reusable code.
For example, `Product,Amount` is a CSV schema; decimal conversion is a type rule;
"refunds are permitted, but absolute amounts must be below a limit" is a business
rule. A parser should not invent the business rule.

Choose a grammar strategy that matches the format. Splitting works for a format
whose delimiter cannot appear inside values. Once escaping or quoting is present,
state matters. A regular expression can be useful for a small local token rule,
but maintaining an entire nested or escaped document grammar in one expression
usually becomes difficult. JSON and XML already have mature parsers.

Be explicit about null versus empty, missing versus default, whitespace,
duplicate keys, case sensitivity, culture, and malformed records. These choices
are part of a format contract. A permissive parser can accept input today that
becomes impossible to interpret consistently tomorrow.

Resource limits belong in that contract too: maximum file bytes, document depth,
record length, field count, and allowed error count. Streaming a single enormous
field can still consume enormous memory. A file size check is only an early
estimate if another writer can modify the file; enforce limits while consuming
when the limit must be strict.

## JSON

Use typed serialization when the data has a stable shape:

```csharp
var candidate = new Candidate
{
    Name = "Zoë",
    Score = 98.75m,
    Skills = ["C#", "IO"]
};
await JsonFiles.SaveAsync("candidate.json", candidate);
Candidate copy = await JsonFiles.LoadAsync<Candidate>("candidate.json");
```

Here, serialization emits camelCase property names. Deserialization rejects
unknown properties and a top-level `null`, while missing properties can retain
model defaults. Those choices are project policy, not universal JSON behavior.
Reusing the options object avoids rebuilding configuration and metadata caches.
See [Microsoft's deserialization guide](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/deserialization)
for serializer behavior and supported input forms.

A document model is useful when you need only a few values or the schema varies:

```csharp
using var document = JsonDocument.Parse("{\"name\":\"Zoë\"}");
string? name = document.RootElement.GetProperty("name").GetString();
```

`JsonDocument` is disposable; its elements depend on its lifetime. Copy values or
clone an element before returning it from a scope that disposes the document.
`JsonNode` provides a mutable tree when editing is the main task. Both approaches
hold a representation in memory.

For larger input, consider top-level array streaming with
`JsonSerializer.DeserializeAsyncEnumerable<T>`. It allows processing elements as
they arrive, but accumulating them afterward removes the memory benefit. For
custom token processing, `Utf8JsonReader` reads UTF-8 tokens without building a
whole tree. Incremental use requires preserving unconsumed bytes and reader
state between buffers; an incomplete token may require a larger buffer. The
[Utf8JsonReader guide](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonreader)
explains those mechanics.

Numbers, timestamps, and enums deserve explicit contracts. Prefer `decimal` for
base-10 amounts where appropriate; use `DateTimeOffset` when an offset matters.
Avoid assuming that arbitrary strings automatically become your preferred enum
or custom type. Configure converters deliberately and validate their output.
A syntactically valid object still needs application validation.

## XML

XML supplies elements, attributes, namespaces, and a document hierarchy.
`XmlSerializer` maps compatible public members to that hierarchy:

```csharp
XmlFiles.Save("candidate.xml", candidate);
Candidate copy = XmlFiles.Load("candidate.xml");
string[] skills = XmlFiles.ReadSkills("candidate.xml");
```

The model is public, has a parameterless constructor, and exposes writable public
properties. The root attribute names the element `candidate`. XML escaping is
handled by the writer; do not hand-escape text before passing it to the serializer.

`XDocument` is convenient for queries and transformations. `XmlReader` is a
forward-only alternative for large documents. Namespace identity is based on
namespace URI plus local name, not the spelling of a prefix:

```csharp
XNamespace ns = "urn:interview";
XElement? name = document.Root?.Element(ns + "Name"); // document is an XDocument.
```

An element called `x:Name` can match that query if `x` refers to the same URI.
An unqualified `Element("Name")` will not match a namespaced element. XML names
are case sensitive.

The project creates readers with DTD processing prohibited, external resolution
disabled, and a document character limit. DTDs and external entities introduce
capabilities that these examples do not need. The same reader settings protect
both typed loading and tree loading.

`XmlSerializer` normally wraps XML parsing failures in
`InvalidOperationException`; its `InnerException` helps explain the cause.
Serializer mapping is not XSD validation and does not establish business
correctness. If an interchange requires a schema, configure schema validation
explicitly and decide whether warnings should reject the document.

## CSV

CSV looks simple because its structure is visually small. Quoting makes it a
stateful grammar. Consider this logical record:

```csv
"Mug, blue","Customer said ""great""",7.00
```

The comma inside the first field is data. Doubled quotes inside the second field
represent literal quotes. A quoted field may also span physical lines. Splitting
on commas or reading one physical line as one record loses these distinctions.

`CsvFiles.Parse` has three meaningful states:

| State | Comma/newline | Quote | Other text |
| --- | --- | --- | --- |
| Unquoted | Finish field/record | Open quoted field only at field start | Append |
| Quoted | Append as data | Double quote becomes data; lone quote closes | Append |
| Just closed | Finish field/record | Reject | Reject |

At EOF, an open quoted field is invalid. A started final record is yielded even
without a final newline. Empty input yields no rows; a blank line yields one empty
field. A trailing comma creates a final empty field. The parser preserves
whitespace and rejects spaces after a closing quote: that is its strict dialect.

```csharp
CsvFiles.Write("sales.csv", new[]
{
    new[] { "Product", "Amount" },
    new[] { "Coffee, dark", "12.50" },
    new[] { "Coffee, dark", "2.25" }
});
var totals = CsvFiles.TotalByProduct("sales.csv");
Console.WriteLine(totals["Coffee, dark"]); // Decimal value 14.75.
```

`ReadSales` validates the exact header, the number of columns, and numeric syntax.
Its invariant culture interprets `12.50` consistently under different current
cultures. Its error location counts logical records, including the header, rather
than physical lines.

`ReadSales` streams; `TotalByProduct` uses `GroupBy` and buffers all sales. A
streaming aggregate is straightforward:

```csharp
var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
foreach (var sale in CsvFiles.ReadSales("sales.csv"))
{
    totals.TryGetValue(sale.Product, out decimal previous);
    totals[sale.Product] = previous + sale.Amount;
}
```

Memory now grows with distinct product names, plus the current record. This does
not make the dictionary unbounded-input safe: distinct keys may still be numerous.

CSV escaping and spreadsheet interpretation are separate. A spreadsheet can treat
`=1+1` as a formula even when properly CSV-quoted. `ForSpreadsheet` adds an apostrophe
to suspicious values as an opt-in transformation. Test that convention with the
target application; preserve raw values when exporting for machine consumers.

## Async IO and cancellation

Async APIs let a caller await operations without synchronously waiting in its
own execution flow. They do not make the disk faster, make parsing parallel, or
establish exclusive access. OS and runtime implementations differ; avoid assuming
that every async file operation uses the same underlying mechanism. Microsoft's
[asynchronous file IO guide](https://learn.microsoft.com/dotnet/standard/io/asynchronous-file-i-o)
shows the standard APIs.

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
Candidate copy = await JsonFiles.LoadAsync<Candidate>(
    "candidate.json", cancellation.Token);
```

Cancellation is cooperative. An operation may have made progress before observing
the token. A canceled direct write may leave partial output. A timeout is also not
proof that another operation has stopped unless you cancel and await that operation.

Await work before disposing the resources it uses. Avoid overlapping operations
on a stream whose position and buffering are shared. For independent files,
bounded concurrency can improve throughput, but opening thousands at once can
exhaust handles and overwhelm storage. Decide the limit using measurement.

`await using` invokes asynchronous disposal where supported. It concerns resource
cleanup; it does not turn synchronous parsing into asynchronous parsing.
`Task.Run` is a scheduling tool, not a substitute for native async APIs or a
solution to file-locking races.

## Sharing, locking, and retry

Access and sharing describe different things. `FileAccess.ReadWrite` says what
this handle may do. `FileShare.Read` says what sharing this open permits. Sharing
compatibility also depends on other handles' access and sharing settings.
`FileShare.ReadWrite` permits access; it does not coordinate offsets, order writes,
or create a consistent snapshot.

The tour holds a file with `FileShare.None`, attempts a competing open, and releases
the owner during a bounded retry. The returned handle must itself be disposed:

```csharp
await using var handle = await FileUtilities.OpenExclusiveAsync("notes.txt");
// Use handle while this scope owns it.
```

By default, the helper retries Windows sharing/lock violations only. Missing files,
permission failures, and disk problems propagate. On Unix, supply a classifier
for errors known to be transient in your environment. The tour's classifier is
restricted to an error observed on its own test file; it is not a universal errno
mapping.

Retries need a maximum, cancellation, and a delay. Real services may add increasing
delays and jitter to reduce synchronized contention. Retrying every `IOException`
can hide permanent failures and increase load. Retrying a complete write also
requires knowing whether an earlier attempt partially succeeded.

An in-process `lock` or `SemaphoreSlim` coordinates participating code in that
process. It does not coordinate another program. For async code, acquire a
`SemaphoreSlim` with `WaitAsync` and release it in `finally`; do not hold a C#
`lock` across an `await`.

Unix file locks are advisory and can be bypassed by programs that do not cooperate.
Byte-range locks are another mechanism, with platform-dependent behavior. See
[Microsoft's file IO implementation discussion](https://devblogs.microsoft.com/dotnet/file-io-improvements-in-dotnet-6/)
and [Unix region-lock behavior](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/filestream-file-locks-unix).
The tour was verified on Linux; Windows-specific behavior needs a Windows run.
If exclusive sharing is not enforced in the current environment, the tour reports
that and skips its lock-dependent checks.

## Replacement, transactions, and durability

Writing directly to an existing file can expose a truncated or partial document.
For an important single-file update, the project stages bytes in a sibling file,
closes it, then moves it over the destination:

```csharp
await FileUtilities.ReplaceTextAsync("settings.txt", "enabled=true\n");
```

Staging beside the destination avoids a cross-volume move. On filesystems with
atomic same-directory replacement, readers opening the name see the old or new
file rather than a half-written destination. This property depends on filesystem
semantics; it is not a universal transaction guarantee.

Several guarantees are easy to confuse:

| Guarantee | Meaning | What staged replacement alone provides |
| --- | --- | --- |
| Atomic visibility | A reader sees a complete version | Often, with appropriate rename semantics |
| Isolation | Concurrent operations do not interfere | No |
| Durability | Committed data survives power loss | No general guarantee |
| Multi-file consistency | Related files change together | No |

Two writers can stage successfully and overwrite each other's results. Protect
an entire read-modify-write cycle if lost updates matter. Locking only the final
write leaves the preceding read vulnerable. A version check also needs coordination
with the commit to avoid another check/use race.

A text writer flush pushes buffered text to its stream. A stream flush can still
leave data in OS/device caches. `FileStream.Flush(flushToDisk: true)` asks for a
stronger flush, but durable replacement can also require filesystem-specific
handling of directory metadata and storage guarantees. Do not claim power-loss
safety from `FlushAsync` plus rename.

Replacement can change metadata. On Windows, open handles can prevent replacement
unless their sharing settings permit it. On Unix, a handle opened before replacement
can continue reading the old file. A consistent multi-file state, transactional
updates, or many competing writers may be better served by a database.

## Large files and advanced tools

Start by asking where memory grows. A file can be streamed while its parser holds
a whole document; records can be streamed while a sort buffers them all. A useful
budget includes byte buffers, decoded text, object graphs, collections, and the
largest individual record.

Binary formats add explicit layout choices: byte order, integer widths, length
prefixes, checksums, versions, and maximum payload sizes. A four-byte length header
must be read exactly, decoded with an agreed endianness, and validated before
allocating a payload buffer. `BinaryPrimitives` makes byte order explicit. Never
trust a length from disk merely because it fits an integer type.

Measure before choosing specialized tools:

| Tool | Useful when | Complexity introduced |
| --- | --- | --- |
| `ArrayPool<byte>` | Repeated buffer allocation is costly | Return buffers in finally; do not retain references after return |
| `RandomAccess` | Reads/writes need explicit independent offsets | Coordinate ranges and handle lifetime |
| `MemoryMappedFile` | Large files need repeated indexed access | Mapping lifetime, address space, concurrent modification |
| `System.IO.Pipelines` | Incremental byte processing needs backpressure | Buffer ownership and consumed/examined positions |
| JSON source generation | Serializer metadata cost or deployment constraints matter | Maintain an explicit supported type set |

These are further-study topics, not implemented helpers in this repository.
Keep the simple implementation until benchmarks identify a problem worth solving.
Do not report buffer throughput as end-to-end import throughput when validation,
allocation, or downstream writes dominate.

`FileSystemWatcher` helps detect changes, but its events are hints rather than a
durable queue. Notifications can repeat, and its event buffer can overflow. A
created event does not prove the producer has finished writing. Handle errors,
rescan when needed, and make processing idempotent. Microsoft's
[FileSystemWatcher documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher)
describes its operational constraints. A producer protocol that writes a temporary
name and publishes a final name can give consumers a much clearer readiness signal.

## Designing a dependable import

Imagine importing a directory of sales files while another application produces
them. Reliability comes from an explicit protocol as much as from parser code.

1. Define how a producer marks a file ready, preferably after finishing its write.
2. Open it directly with the sharing policy you intend. Classify access failures.
3. Enforce byte, record, and field limits during consumption.
4. Parse structure, then convert values with explicit culture and type rules.
5. Validate required fields and business constraints with record-level diagnostics.
6. Decide whether one bad record rejects the whole import or only that record.
7. Coordinate destination updates and record an import identity where needed.
8. Publish success only after the chosen commit completes; clean up resources.

If you save rows incrementally and later reject the file, earlier rows may already
be committed. Whole-file acceptance requires staging or transaction support in
the destination. Incremental acceptance requires documenting partial success.
Neither policy is inherently correct for every application.

Useful diagnostics identify the file, stage, logical record, field, and cause.
Preserve the original exception when wrapping it. Avoid logging entire documents
when a concise location and reason suffice. For repeated ingestion, design
idempotency: processing the same completed import again should have a defined
result, rather than accidentally doubling totals.

## Interview questions and exercises

A good answer states the failure mechanism, the handling strategy, and the limit
of that strategy. For example: "A single Read can return a partial buffer, so I
loop or use ReadExactly. If EOF arrives before the expected length, I reject a
truncated record." That explains behavior rather than just naming a method.

| Question | Point to explain | Project example |
| --- | --- | --- |
| Why did a short overwrite leave old text? | Opening without truncation preserves the old length | `StreamsAsync` |
| Why is valid JSON still invalid data? | Syntax, type conversion, and business validation differ | `JsonFiles.LoadAsync` |
| Why cannot CSV be split by line then comma? | Delimiters and newlines can be quoted data | `CsvFiles.Parse` |
| Who closes a passed stream? | Define ownership; use leaveOpen when appropriate | MemoryStream example |
| Why can a lazy read throw after its method returned? | Work occurs during enumeration | `Read` and `ReadSales` |
| Does File.Exists avoid an open failure? | State can change; existence does not imply permission | `CreateNew` example |
| Does async make concurrent writes safe? | Scheduling and coordination are separate | `OpenExclusiveAsync` |
| Does rename prevent lost updates? | Visibility does not imply isolation | `ReplaceTextAsync` |
| Can a normalized path still escape through a symlink? | Lexical checks do not resolve filesystem identity | `ResolveUnderDirectory` |
| Why does a watcher need reconciliation? | Events can repeat or be lost | Advanced discussion above |

Try these exercises after reading the code:

1. Replace the LINQ sales aggregate with dictionary accumulation. Compare memory
   on many rows with few products, then on many distinct products.
2. Add a configurable CSV field-size limit and verify it triggers inside a quoted
   multiline field, not only after the field is already allocated.
3. Add Candidate validation that distinguishes missing names from invalid scores.
   Keep parse errors and validation errors separate.
4. Add an XML namespace to a sample and update its document queries correctly.
5. Build a small binary record format with a fixed-size length prefix. Test partial
   header reads, premature payload EOF, negative lengths, and oversized lengths.
6. Adapt the serialization save path to sibling staging, then inject a failure
   before publication and verify the old destination remains readable.
7. Cancel a lock retry during its delay and verify prompt cancellation and cleanup.
8. Run the tour on Windows and compare lock and replacement behavior with Linux.

Read [the focused File IO guide](FileIO/README.md) for the implementation-specific
notes, then inspect [serialization](FileIO/SerializationExamples.cs),
[CSV](FileIO/CsvFiles.cs), [utilities](FileIO/FileUtilities.cs), and
[the executable tour](FileIO/FileIOTour.cs). Each source file has a header explaining
its purpose, mechanism, examples, and limitations.
