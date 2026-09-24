namespace Adventures.Data.NQuad;

/// <summary>
/// Parses W3C N-Quads text (one statement per line, ".nq" format) into <see cref="NQuad"/>
/// records. Adapted from the nquad-end-to-end-poc's NQuadParser - same grammar subset
/// (angle-bracketed IRIs, quoted literals with escapes/language tags/datatypes, optional
/// graph term), trimmed to what this library currently needs.
/// </summary>
public sealed class NQuadFileParser
{
    private readonly Func<Guid> _idFactory;

    public NQuadFileParser(Func<Guid>? idFactory = null)
    {
        _idFactory = idFactory ?? Guid.NewGuid;
    }

    public IReadOnlyList<NQuad> Parse(string nQuads)
    {
        ArgumentNullException.ThrowIfNull(nQuads);

        var results = new List<NQuad>();
        using var reader = new StringReader(nQuads);
        string? line;
        var lineNumber = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            results.Add(ParseLine(line, lineNumber));
        }

        return results;
    }

    private NQuad ParseLine(string line, int lineNumber)
    {
        var position = 0;
        var subject = ReadIri(line, ref position, lineNumber, "subject");
        SkipWhitespace(line, ref position);
        var predicate = ReadIri(line, ref position, lineNumber, "predicate");
        SkipWhitespace(line, ref position);
        var value = ReadObject(line, ref position, lineNumber);
        SkipWhitespace(line, ref position);

        string? graph = null;
        if (position < line.Length && line[position] != '.')
        {
            graph = ReadIri(line, ref position, lineNumber, "graph");
            SkipWhitespace(line, ref position);
        }

        if (position >= line.Length || line[position] != '.')
        {
            throw SyntaxError(lineNumber, "Expected a terminating period.");
        }

        return new NQuad(_idFactory(), subject, predicate, value, graph);
    }

    private static string ReadObject(string line, ref int position, int lineNumber)
    {
        if (position >= line.Length)
        {
            throw SyntaxError(lineNumber, "Expected an object.");
        }

        return line[position] == '<'
            ? ReadIri(line, ref position, lineNumber, "object")
            : ReadLiteral(line, ref position, lineNumber);
    }

    private static string ReadIri(string line, ref int position, int lineNumber, string termName)
    {
        if (position >= line.Length || line[position] != '<')
        {
            throw SyntaxError(lineNumber, $"Expected an IRI for the {termName}.");
        }

        var start = ++position;
        while (position < line.Length && line[position] != '>')
        {
            position++;
        }

        if (position >= line.Length)
        {
            throw SyntaxError(lineNumber, $"The {termName} IRI is not closed.");
        }

        var iri = line[start..position];
        position++;
        return iri;
    }

    private static string ReadLiteral(string line, ref int position, int lineNumber)
    {
        if (line[position] != '"')
        {
            throw SyntaxError(lineNumber, "Expected an IRI or quoted literal for the object.");
        }

        position++;
        var value = new System.Text.StringBuilder();
        var closed = false;
        while (position < line.Length)
        {
            var character = line[position++];
            if (character == '"')
            {
                closed = true;
                break;
            }

            if (character == '\\')
            {
                if (position >= line.Length)
                {
                    break;
                }

                var escaped = line[position++];
                value.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    _ => throw SyntaxError(lineNumber, $"Unsupported literal escape '\\{escaped}'.")
                });
            }
            else
            {
                value.Append(character);
            }
        }

        if (!closed)
        {
            throw SyntaxError(lineNumber, "The literal is not closed.");
        }

        if (position < line.Length && line[position] == '@')
        {
            position++;
            while (position < line.Length && (char.IsLetterOrDigit(line[position]) || line[position] == '-'))
            {
                position++;
            }
        }
        else if (position + 1 < line.Length && line[position] == '^' && line[position + 1] == '^')
        {
            position += 2;
            SkipWhitespace(line, ref position);
            _ = ReadIri(line, ref position, lineNumber, "literal datatype");
        }

        return value.ToString();
    }

    private static void SkipWhitespace(string line, ref int position)
    {
        while (position < line.Length && char.IsWhiteSpace(line[position]))
        {
            position++;
        }
    }

    private static FormatException SyntaxError(int lineNumber, string message) =>
        new($"Invalid N-Quad at line {lineNumber}: {message}");
}

