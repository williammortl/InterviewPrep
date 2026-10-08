/*
 * CSV: parsing, writing, and turning text into useful data
 * ------------------------------------------------------
 * What: CsvFiles reads/writes a strict comma-separated dialect and imports sales.
 * Shows: why Split(',') fails for quoted commas and multiline records; how lazy
 * iterators own files; why numeric imports should use an explicit culture; and
 * why CSV quoting alone does not make spreadsheet formulas safe.
 * How: Parse walks characters with three states (unquoted, quoted, just closed).
 * It buffers one record and yields an independent string[] at each record end.
 * Write reverses the escaping rules. ReadSales adds header/type validation, and
 * TotalByProduct demonstrates a LINQ aggregation that buffers its input.
 * Use:
 *   CsvFiles.Write("sales.csv", new[] {
 *       new[] { "Product", "Amount" }, new[] { "Coffee, dark", "12.50" } });
 *   foreach (var sale in CsvFiles.ReadSales("sales.csv"))
 *       Console.WriteLine($"{sale.Product}: {sale.Amount}");
 *   var totals = CsvFiles.TotalByProduct("sales.csv");
 * Read accepts a path; Parse accepts a caller-owned TextReader for in-memory
 * examples. Errors in lazy readers occur while enumerating. Write overwrites
 * the destination and can leave partial output on failure. Parent directories
 * must exist. Run FileIOTour.RunAsync() for successful and failing examples.
 * Sample files: FileIO/Examples (see its README for expected results).
 * Run `dotnet run` to check the shipped samples automatically. For manual
 * reads, resolve paths from the repo root or AppContext.BaseDirectory; save
 * outputs in a temporary directory to keep these fixtures reusable.
 */

using System.Globalization;
using System.Text;

namespace InterviewToolkit.FileIO;

/// <summary>
/// Streaming CSV state machine: commas, doubled quotes, embedded CR/LF, empty
/// fields, and a final record without a newline. Never use Split(',') for CSV.
/// This strict comma dialect rejects quotes in unquoted fields and text after
/// a closing quote; it preserves whitespace and blank records.
/// </summary>
public static class CsvFiles
{
    /// <summary>Enumerate records from a BOM-aware text file; owns and disposes its reader.</summary>
    /// <remarks>IO and syntax errors surface during enumeration, not when Read is called.</remarks>
    public static IEnumerable<string[]> Read(string path)
    {
        // Iterator lifetime owns the reader. foreach/break disposes it, but a
        // manually obtained enumerator must also be disposed.
        using var reader = new StreamReader(path, new UTF8Encoding(false, true), true);
        foreach (var row in Parse(reader))
            yield return row;
    }

    /// <summary>Parse records lazily without closing the caller-owned reader.</summary>
    /// <exception cref="FormatException">The input violates this strict CSV dialect.</exception>
    public static IEnumerable<string[]> Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var row = new List<string>();
        var field = new StringBuilder();
        // quoted: delimiters are data; closed: only a delimiter or EOF is legal.
        // started distinguishes empty input from an empty field or trailing comma.
        bool quoted = false, closed = false, started = false;
        while (reader.Read() is int next && next != -1)
        {
            char c = (char)next;
            started = true;
            if (quoted)
            {
                if (c == '"')
                {
                    // Consume doubled quotes together; a lone quote closes the field.
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else { quoted = false; closed = true; }
                }
                else field.Append(c);
                continue;
            }
            // Reaching this branch means we are outside quotes, so delimiters
            // may end a field or record. CRLF counts as one record terminator.
            if (c == ',' || c == '\r' || c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                closed = false;
                if (c == ',') continue;
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                // Copy before clearing: previously yielded rows must remain stable.
                yield return row.ToArray();
                row.Clear();
                started = false;
            }
            else if (closed) throw new FormatException("Unexpected text after a closing CSV quote.");
            else if (c == '"')
            {
                if (field.Length != 0) throw new FormatException("Quote inside an unquoted CSV field.");
                quoted = true;
            }
            else field.Append(c);
        }
        // EOF is valid after a closing quote or an unquoted field, but never
        // inside quotes. Flush only if a record exists (including a trailing comma).
        if (quoted) throw new FormatException("Unterminated quoted CSV field.");
        if (started) { row.Add(field.ToString()); yield return row.ToArray(); }
    }

    /// <summary>Overwrite a file with escaped records in UTF-8 without a BOM.</summary>
    /// <remarks>Uses the platform newline; disposal flushes buffered output.</remarks>
    public static void Write(string path, IEnumerable<IEnumerable<string>> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false, true));
        foreach (var row in rows)
        {
            var fields = row.ToArray();
            if (fields.Length == 0) throw new ArgumentException("A CSV record must have at least one field.");
            writer.WriteLine(string.Join(",", fields.Select(Escape)));
        }
    }

    /// <summary>Encode one field: quote delimiters/newlines and double embedded quotes.</summary>
    public static string Escape(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }

    // A spreadsheet may execute a quoted CSV field beginning with '=' as a
    // formula. This opt-in transformation changes data; CSV quoting alone does
    // not prevent formula injection. Verify against the target spreadsheet app.
    /// <summary>Opt-in apostrophe prefix for possible formulas; apply before Write/Escape.</summary>
    public static string ForSpreadsheet(string value) =>
        value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') ||
        value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@') ||
        value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')
            ? "'" + value : value;

    /// <summary>A typed imported row; decimal avoids binary rounding for money-like values.</summary>
    public readonly record struct Sale(string Product, decimal Amount);

    /// <summary>Require Product,Amount headers and parse amounts with invariant decimal syntax.</summary>
    /// <remarks>Reported record numbers count logical records, including the header, not physical lines.</remarks>
    public static IEnumerable<Sale> ReadSales(string path)
    {
        using var rows = Read(path).GetEnumerator();
        if (!rows.MoveNext() || !rows.Current.SequenceEqual(new[] { "Product", "Amount" }))
            throw new FormatException("Expected Product,Amount header.");
        int record = 1;
        while (rows.MoveNext())
        {
            record++;
            var fields = rows.Current;
            if (fields.Length != 2 || !decimal.TryParse(fields[1],
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount))
                throw new FormatException($"Invalid sale at record {record}.");
            yield return new Sale(fields[0], amount);
        }
    }

    /// <summary>Sum sales by case-sensitive product name.</summary>
    /// <remarks>GroupBy buffers all sales; a per-row Dictionary update would use less memory.</remarks>
    public static Dictionary<string, decimal> TotalByProduct(string path) => ReadSales(path)
        .GroupBy(sale => sale.Product, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Sum(sale => sale.Amount), StringComparer.Ordinal);
}
