using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MonitovoPDF.Rendering;

/// <summary>
/// Works out where a page draws each of its XObjects, by following the content stream.
/// </summary>
/// <remarks>
/// <para>
/// An XObject carries no position of its own. The page decides, by setting a transform and then
/// naming the object — so the only way to report where a placeholder sits is to read the
/// instructions and track the transform in force at each drawing operator.
/// </para>
/// <para>
/// This follows the operators that affect placement and ignores everything else. A page that
/// draws through a nested form, or under an unusual construction, may not be resolved; that is
/// reported as no known position rather than as a guess.
/// </para>
/// </remarks>
internal static class PlacedXObjects
{
    /// <summary>The identity transform, and the six numbers a transform is written as.</summary>
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);

        /// <summary>Applies this transform, then <paramref name="outer"/>.</summary>
        public Matrix Then(Matrix outer) => new(
            (A * outer.A) + (B * outer.C),
            (A * outer.B) + (B * outer.D),
            (C * outer.A) + (D * outer.C),
            (C * outer.B) + (D * outer.D),
            (E * outer.A) + (F * outer.C) + outer.E,
            (E * outer.B) + (F * outer.D) + outer.F);

        public (double X, double Y) Apply(double x, double y) =>
            ((A * x) + (C * y) + E, (B * x) + (D * y) + F);
    }

    /// <summary>Returns the rectangles each XObject name is drawn into, in page coordinates.</summary>
    public static Dictionary<string, List<XRect>> On(PdfPage page)
    {
        var found = new Dictionary<string, List<XRect>>(StringComparer.Ordinal);

        var content = Content(page);
        if (content.Length == 0)
            return found;

        var stack = new Stack<Matrix>();
        var current = Matrix.Identity;
        var operands = new List<double>();
        var lastName = (string?)null;

        foreach (var token in Tokenise(content))
        {
            if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                operands.Add(number);
                continue;
            }

            if (token.StartsWith('/'))
            {
                lastName = token;
                continue;
            }

            switch (token)
            {
                case "q":
                    stack.Push(current);
                    break;

                case "Q":
                    current = stack.Count > 0 ? stack.Pop() : Matrix.Identity;
                    break;

                case "cm" when operands.Count >= 6:
                    var tail = operands.GetRange(operands.Count - 6, 6);
                    current = new Matrix(tail[0], tail[1], tail[2], tail[3], tail[4], tail[5]).Then(current);
                    break;

                case "Do" when lastName is not null:
                    if (!found.TryGetValue(lastName, out var rectangles))
                        found[lastName] = rectangles = [];

                    rectangles.Add(BoundsOf(current, page));
                    break;
            }

            operands.Clear();
        }

        return found;
    }

    /// <summary>Maps the unit square through the transform and takes its bounding box.</summary>
    private static XRect BoundsOf(Matrix matrix, PdfPage page)
    {
        (double X, double Y)[] corners =
        [
            matrix.Apply(0, 0), matrix.Apply(1, 0), matrix.Apply(0, 1), matrix.Apply(1, 1),
        ];

        var left = corners.Min(corner => corner.X);
        var right = corners.Max(corner => corner.X);
        var bottom = corners.Min(corner => corner.Y);
        var top = corners.Max(corner => corner.Y);

        // Reported from the top of the page, to match how field placements are reported.
        return new XRect(left, page.Height.Point - top, right - left, top - bottom);
    }

    private static string Content(PdfPage page)
    {
        var text = new System.Text.StringBuilder();

        foreach (var stream in page.Contents)
            text.Append(System.Text.Encoding.Latin1.GetString(stream.Stream.UnfilteredValue));

        return text.ToString();
    }

    /// <summary>
    /// Splits a content stream into operands and operators, stepping over the things that only
    /// look like them: text inside brackets, hex inside angles, and comments.
    /// </summary>
    private static IEnumerable<string> Tokenise(string content)
    {
        var index = 0;

        while (index < content.Length)
        {
            var character = content[index];

            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            switch (character)
            {
                case '%':
                    while (index < content.Length && content[index] is not ('\n' or '\r'))
                        index++;

                    continue;

                case '(':
                    // Bracketed text may contain anything, including unbalanced-looking escapes.
                    var depth = 0;
                    while (index < content.Length)
                    {
                        if (content[index] == '\\') index += 2;
                        else if (content[index] == '(') { depth++; index++; }
                        else if (content[index] == ')') { depth--; index++; if (depth == 0) break; }
                        else index++;
                    }

                    continue;

                case '<' when index + 1 < content.Length && content[index + 1] != '<':
                    while (index < content.Length && content[index] != '>')
                        index++;

                    index++;
                    continue;

                case '<' or '>' or '[' or ']' or '{' or '}':
                    index++;
                    continue;
            }

            var start = index;
            while (index < content.Length
                   && !char.IsWhiteSpace(content[index])
                   && content[index] is not ('(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
            {
                index++;
            }

            if (index > start)
                yield return content[start..index];
            else
                index++;
        }
    }
}
