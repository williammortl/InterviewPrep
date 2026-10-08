/*
 * JSON and XML: object round trips versus document queries
 * -------------------------------------------------------
 * What: Candidate is the shared model; JsonFiles and XmlFiles save, load, and
 * query it with .NET's built-in serializers and document APIs.
 * Shows: typed deserialization, Unicode/escaping, JSON naming options, document
 * lifetime, XML reader restrictions, and differences in parse error reporting.
 * How: JSON serializes directly to async FileStreams and uses JsonDocument for
 * queries. XML maps public properties with XmlSerializer and queries XDocument.
 * Both approaches dispose every stream/reader/document they create.
 * Use:
 *   var candidate = new Candidate { Name = "Zoë", Score = 98.75m,
 *       Skills = new List<string> { "C#", "IO" } };
 *   await JsonFiles.SaveAsync("candidate.json", candidate);
 *   var jsonCopy = await JsonFiles.LoadAsync<Candidate>("candidate.json");
 *   XmlFiles.Save("candidate.xml", candidate);
 *   var xmlCopy = XmlFiles.Load("candidate.xml");
 *   var skills = XmlFiles.ReadSkills("candidate.xml");
 * Save overwrites directly; parent directories must exist. Parsing is not
 * business validation: missing properties can retain defaults. Document queries
 * load the document into memory. See FileIOTour for malformed-input examples.
 * Sample files: FileIO/Examples (see its README for expected results).
 * Run `dotnet run` to check the shipped samples automatically. For manual
 * reads, resolve paths from the repo root or AppContext.BaseDirectory; save
 * outputs in a temporary directory to keep these fixtures reusable.
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace InterviewToolkit.FileIO;

// XmlSerializer needs a public type, a parameterless constructor, and writable
// public properties. A single model makes the JSON/XML round trip easy to compare.
/// <summary>Shared public property model for comparing JSON and XML round trips.</summary>
[XmlRoot("candidate")]
public sealed class Candidate
{
    public string Name { get; set; } = "";
    public decimal Score { get; set; }
    public List<string> Skills { get; set; } = [];
}

/// <summary>Typed serialization versus document parsing, using only the BCL.</summary>
public static class JsonFiles
{
    // Options are reused: metadata caching matters when serializing repeatedly.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Overwrite JSON using camelCase properties; cancellation may leave partial output.</summary>
    public static async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
    }

    /// <summary>Load a typed value; reject unknown members and a top-level null.</summary>
    /// <remarks>Missing model properties can keep their defaults; validate business rules separately.</remarks>
    public static async Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken)
            ?? throw new JsonException("The document contained null, not an object.");
    }

    /// <summary>Query the skills array without deserializing a Candidate; return owned strings.</summary>
    public static async Task<string[]> ReadSkillsAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream);
        // JsonElement normally borrows the document's memory. Materialize strings
        // before Dispose; return element.Clone() if returning a subtree instead.
        return document.RootElement.GetProperty("skills").EnumerateArray()
            .Select(element => element.GetString() ?? throw new JsonException("Null skill.")).ToArray();
    }
}

/// <summary>Candidate XML round trips and LINQ to XML queries through a restricted reader.</summary>
public static class XmlFiles
{
    private static readonly XmlSerializer Serializer = new(typeof(Candidate));

    // Share the same restrictions between typed and document-based loading.
    // No DTD/entity resolution, and a character limit bounds document size.
    private static XmlReader OpenReader(string path) => XmlReader.Create(path, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 1_000_000
    });

    /// <summary>Overwrite UTF-8 XML; XmlSerializer handles character escaping.</summary>
    public static void Save(string path, Candidate candidate)
    {
        using var writer = XmlWriter.Create(path, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new System.Text.UTF8Encoding(false, true)
        });
        Serializer.Serialize(writer, candidate);
    }

    /// <summary>Deserialize a Candidate; malformed XML is wrapped in InvalidOperationException.</summary>
    public static Candidate Load(string path)
    {
        using var reader = OpenReader(path);
        return (Candidate)(Serializer.Deserialize(reader)
            ?? throw new InvalidDataException("Missing candidate."));
    }

    /// <summary>Query the serializer-generated, unnamespaced Skills/string elements.</summary>
    /// <remarks>Returns an empty array if the expected skills container is absent.</remarks>
    public static string[] ReadSkills(string path)
    {
        using var reader = OpenReader(path);
        var document = XDocument.Load(reader);
        // XML names are case sensitive. For namespaced input use XNamespace + name.
        return document.Root?.Element("Skills")?.Elements("string")
            .Select(element => element.Value).ToArray() ?? [];
    }
}
