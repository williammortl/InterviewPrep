# A tour of file IO in C#

Run `dotnet run` from the project root. The existing algorithm/data structure
examples run first, followed by `FileIOTour.RunAsync()`. Checks throw on failure.
The tour creates its own temporary directory and deletes it in `finally`;
sample input files ship with the project; no external files or NuGet packages are needed.

## Testing the example files

The [Examples folder](Examples/README.md) includes valid candidates in JSON/XML,
CSV sales and boundary cases, text search input, and malformed samples with known
failure outcomes. `dotnet run` checks them automatically before the generated
corner-case demonstrations. Build/publish copies the examples beside the program;
the tour resolves `FileIO/Examples` relative to `AppContext.BaseDirectory`.

For manual tests, pass a sample path to the appropriate load/read method, fully
enumerate CSV inputs, and compare the results listed in the
[sample inventory](Examples/README.md#sample-inventory). Save serialized or CSV
output into a temporary directory and reload it; keep input fixtures unchanged.
The sample guide includes copyable snippets for both reading and writing.

## Suggested reading order

1. **SerializationExamples.cs** — `Candidate`, `JsonFiles`, and `XmlFiles`.
   Save a typed object, load it back, then query the document without binding
   everything to a model. JSON uses async streams and a reusable options object;
   XML uses `XmlSerializer` and LINQ to XML through a restricted `XmlReader`.
2. **CsvFiles.cs** — a character-by-character CSV parser and matching writer,
   typed sales validation, aggregation, and an opt-in spreadsheet export helper.
3. **FileUtilities.cs** — staged replacement, exclusive-open retries,
   exact-length reads, path resolution, and lazy line filtering.
4. **FileIOTour.cs** — working examples, intentionally failing operations, and
   checks showing how to recover. Change the examples and rerun to experiment.

## Serialization: bytes, syntax, objects, and validation

These are separate concerns. A valid document can still represent invalid
business data. JSON loading rejects unknown properties and top-level `null`,
but missing Candidate properties retain their defaults. XML serialization does
not enforce a business schema and normally ignores unknown elements. Validate
required names, allowed scores, and skills after loading if your application
needs those rules. Both formats preserve the example's decimal score and Unicode
name; XML automatically escapes `&` and `<`.

`JsonDocument` owns the memory behind its elements. Return copied values or
`JsonElement.Clone()` before disposing it. Use a typed model when the schema is
known, a document when inspecting an irregular structure, and a streaming
reader for documents too large to hold in memory. The query examples materialize
the document; async IO does not make document parsing memory-free.

XML names are case sensitive and namespace-aware. For `<c:Skills>` in a namespace,
use `XNamespace ns = "the namespace URI";` and `Element(ns + "Skills")`.
The reader prohibits DTDs and external resolution and caps document size.
`XmlSerializer` wraps parse failures in `InvalidOperationException`; inspect
`InnerException` for the underlying XML error. JSON syntax/type errors usually
surface as `JsonException`; file-access failures are separate exceptions.

The simple save functions use `FileMode.Create`, so they truncate immediately:
a failure can leave a partial file. `ReplaceTextAsync` demonstrates staging for
an important destination; the same staging pattern can wrap serialization.

## CSV is a grammar, not a string split

The parser tracks unquoted, quoted, and just-closed fields. Only delimiters
outside quotes finish fields or records. Inside a quoted field, `""` means one
literal quote and CR/LF is data. It accepts CR, LF, and CRLF record endings,
empty fields, trailing commas, and a final record without a newline. Empty input
has zero records; a blank line is a record with one empty field.

It deliberately rejects unterminated quotes, quotes inside unquoted fields,
and characters (including spaces) after a closing quote. This is a strict comma
dialect, not automatic detection of every vendor's CSV format. Text file reading
auto-detects BOMs, defaults to strict UTF-8, and writing uses UTF-8 without a BOM.

`Read` and `ReadSales` yield one record at a time. Memory scales with the largest
record, not the whole file; impose a record-size limit for untrusted huge input.
`TotalByProduct` uses LINQ GroupBy, which buffers all sales. For a truly streaming
aggregate, update a Dictionary for each row (memory then scales with distinct
products). Headers are checked exactly and amounts use invariant culture:
`12.50` has the same meaning even when the current culture uses decimal commas.

Quoting is syntax escaping, not spreadsheet formula protection. A spreadsheet
can interpret `=1+1` even inside a quoted field. `ForSpreadsheet` prefixes
suspicious values with an apostrophe; it is opt-in because it changes data and
must be verified with the target application. Keep raw values for machine
imports. For production dialect support, compare a maintained CSV library with
this deliberately readable parser.

## Interview corner cases

| Question | Demonstration / lesson |
| --- | --- |
| Why did a shorter write leave old characters? | `OpenOrCreate` does not truncate. Use `Create`, explicitly `SetLength`, or staged replacement. |
| Why is my newly written file empty? | Text writers buffer. Flush or dispose before consuming output. `leaveOpen` controls ownership of the underlying stream. |
| Why did a read return no bytes? | The stream position may already be at EOF. Rewind a seekable stream before reading. |
| Can one Read fill my buffer? | No. The tour's stream returns one byte per call. `ReadExactlyAsync` loops and detects premature EOF. |
| Why did Unicode become replacement characters? | Default decoding can replace invalid bytes. Strict UTF-8 throws `DecoderFallbackException`. |
| Is File.Exists then Create safe? | Another process can act between the two. `CreateNew` performs exclusive creation; opening directly also preserves real access errors. |
| Can a lazy reader still hold a file open? | Yes, until its enumerator is disposed. `foreach` disposes on completion or break; manual enumerators need `using`. |
| Does Path.Combine confine a path to a root? | Rooted input can discard the root and `..` can escape it. The helper checks normalized lexical containment; symlinks require a stronger approach. |
| Does async prevent races? | No. It avoids blocking a caller while waiting for IO; synchronization and consistency remain separate problems. |

## Locks, sharing, and replacement

`FileMode` chooses creation/truncation behavior, `FileAccess` chooses what this
handle can do, and `FileShare` chooses allowed sharing. `FileShare.ReadWrite`
allows concurrent access; it does not synchronize writers or provide a consistent
snapshot. `FileShare.None` is used in the tour to provoke a competing-open error.
Always dispose the handle that owns the lock, even on exceptions.

Windows sharing violations have recognizable error codes. The general retry
helper retries only those by default, bounds its attempts, and accepts
cancellation. A missing file, denied permission, or full disk should not be
blindly retried as a lock failure. Unix mappings and filesystem behavior vary;
the demo passes a classifier for an error it just observed on its own file,
then releases the holder on the first retry. If exclusive sharing is not enforced
in an environment, it reports that and skips the lock-dependent checks.

Unix file locks are advisory; uncooperative programs can bypass them. Region
locks (`FileStream.Lock/Unlock`) are another mechanism and have platform-specific
behavior; opening a file exclusively is not the same as locking a byte range.
The tour was verified on Linux; Windows behavior is documented but not tested here.

Staging in the destination directory avoids cross-volume moves. On filesystems
with atomic replacement, readers see the old or new destination rather than a
partial rewrite. It still does not prevent lost updates, coordinate multiple
writers, guarantee power-loss durability, or preserve destination metadata.
An open destination can prevent replacement on Windows unless sharing permits
it. Open handles on Unix can continue reading the old file after a rename.
For read-modify-write workflows, coordinate the whole operation, not just the
final write. For stronger transactions, consider a database.

## Reference material

- [FileStream API](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream)
- [Microsoft's file IO discussion, including advisory Unix locks](https://devblogs.microsoft.com/dotnet/file-io-improvements-in-dotnet-6/)
- [Unix region lock behavior](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/filestream-file-locks-unix)
