using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;

namespace MonitovoPDF.Rendering;

/// <summary>
/// Draws a widget's own appearance for a chosen state, and reads the states a field offers.
/// </summary>
/// <remarks>
/// <para>
/// A tick box is not a character drawn in a box. The template carries a small piece of artwork
/// for each state the box can be in — empty, and ticked — and the tick, the box around it and
/// the weight of both are the designer's, not ours. Drawing that artwork is therefore both the
/// faithful answer and the easy one.
/// </para>
/// <para>
/// It also repairs something flattening would otherwise break. The box's outline usually lives
/// in the widget rather than in the page, so removing the form removes the box along with it.
/// Painting the state's artwork onto the page puts it back, which is why an unticked box is
/// drawn rather than skipped.
/// </para>
/// </remarks>
internal static class FieldAppearances
{
    /// <summary>The name a state carries when the box is empty.</summary>
    public const string OffState = "/Off";

    /// <summary>The states a widget has artwork for, in the order the template lists them.</summary>
    public static List<string> StatesOf(PdfDictionary widget)
    {
        var normal = widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
        if (normal is null)
            return [];

        // A widget with a single appearance rather than one per state has nothing to choose
        // between; a stream where the state dictionary would be is that case.
        return normal.Stream is not null ? [] : [.. normal.Elements.Keys];
    }

    /// <summary>The state that means "ticked": whichever one is not the off state.</summary>
    public static string? OnStateOf(PdfDictionary widget) =>
        StatesOf(widget).FirstOrDefault(state => state != OffState);

    /// <summary>
    /// The values a field accepts, or an empty list when it does not constrain them.
    /// </summary>
    /// <remarks>
    /// A dropdown carries its own list in <c>/Opt</c> — the options belong to the template, not
    /// to the caller, so filling one is choosing from what is already there. A set of radio
    /// buttons has no such list; its options are the state names its widgets answer to, which
    /// come to the same thing.
    /// </remarks>
    public static List<string> OptionsOf(PdfAcroField field)
    {
        if (field.Elements.GetArray("/Opt") is { } options)
        {
            var listed = new List<string>();

            for (var i = 0; i < options.Elements.Count; i++)
            {
                // An entry is either the value itself, or a pair of the value to store and the
                // text to show. Where they differ the shown text is what a person picked.
                var entry = options.Elements[i] is PdfReference reference
                    ? reference.Value
                    : options.Elements[i];

                var value = entry switch
                {
                    PdfArray pair when pair.Elements.Count > 1 => pair.Elements.GetString(1),
                    PdfArray pair when pair.Elements.Count > 0 => pair.Elements.GetString(0),
                    PdfString text => text.Value,
                    _ => null
                };

                if (!string.IsNullOrEmpty(value))
                    listed.Add(value);
            }

            return listed;
        }

        // A radio group's options are the states its buttons answer to.
        var states = new List<string>();
        var kids = field.Elements.GetArray("/Kids");

        for (var i = 0; i < (kids?.Elements.Count ?? 0); i++)
        {
            if (kids!.Elements[i] is not PdfReference { Value: PdfDictionary widget })
                continue;

            foreach (var state in StatesOf(widget))
            {
                if (state != OffState && !states.Contains(state[1..], StringComparer.Ordinal))
                    states.Add(state[1..]);
            }
        }

        return states;
    }

    /// <summary>
    /// Paints one widget's artwork for <paramref name="state"/> onto the page it sits on.
    /// </summary>
    /// <remarks>
    /// The artwork is a form XObject with a coordinate space of its own, so it has to be mapped
    /// onto the rectangle the widget occupies: its bounding box is put through its own matrix,
    /// and what comes out is scaled and shifted to fill the rectangle. That is the mapping the
    /// PDF specification prescribes for exactly this, and following it is what makes the result
    /// identical to what a viewer would have shown.
    /// </remarks>
    /// <returns>Whether there was artwork for that state to draw.</returns>
    public static bool Draw(PdfPage page, PdfDictionary widget, string state)
    {
        var normal = widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
        var item = normal?.Elements.GetValue(state);

        var artwork = item switch
        {
            PdfReference { Value: PdfDictionary resolved } => resolved,
            PdfDictionary direct => direct,
            _ => null
        };

        if (artwork?.Stream is null)
            return false;

        var rect = widget.Elements.GetRectangle("/Rect");
        if (rect.Width <= 0 || rect.Height <= 0)
            return false;

        var name = Register(page, artwork);
        if (name is null)
            return false;

        var (a, b, c, d, e, f) = MapToRect(artwork, rect);

        var drawing = new StringBuilder("q ")
            .Append(Number(a)).Append(' ').Append(Number(b)).Append(' ')
            .Append(Number(c)).Append(' ').Append(Number(d)).Append(' ')
            .Append(Number(e)).Append(' ').Append(Number(f)).Append(" cm ")
            .Append(name).Append(" Do Q\n");

        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(drawing.ToString()));
        return true;
    }

    /// <summary>
    /// Draws a plain box, and a cross in it when ticked, for a widget carrying no artwork.
    /// </summary>
    /// <remarks>
    /// Templates authored by tools that expect the viewer to render their controls leave the
    /// states empty. Flattening such a template would otherwise lose the box entirely, so
    /// something has to be drawn — a cross rather than a tick, because a cross is two straight
    /// lines and survives a low-resolution printer that would turn a tick's thin tail to nothing.
    /// </remarks>
    public static void DrawFallback(PdfPage page, PdfDictionary widget, bool ticked)
    {
        var rect = widget.Elements.GetRectangle("/Rect");
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var x = Math.Min(rect.X1, rect.X2);
        var y = Math.Min(rect.Y1, rect.Y2);
        var width = rect.Width;
        var height = rect.Height;

        // A line weight proportional to the box, so it reads the same at any size.
        var weight = Math.Max(0.4, Math.Min(width, height) / 12);
        var inset = Math.Min(width, height) / 4;

        var drawing = new StringBuilder("q 0 G ")
            .Append(Number(weight)).Append(" w ")
            .Append(Number(x + (weight / 2))).Append(' ').Append(Number(y + (weight / 2))).Append(' ')
            .Append(Number(width - weight)).Append(' ').Append(Number(height - weight)).Append(" re S ");

        if (ticked)
        {
            drawing
                .Append(Number(x + inset)).Append(' ').Append(Number(y + inset)).Append(" m ")
                .Append(Number(x + width - inset)).Append(' ').Append(Number(y + height - inset)).Append(" l S ")
                .Append(Number(x + inset)).Append(' ').Append(Number(y + height - inset)).Append(" m ")
                .Append(Number(x + width - inset)).Append(' ').Append(Number(y + inset)).Append(" l S ");
        }

        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(drawing.Append("Q\n").ToString()));
    }

    /// <summary>
    /// Works out the transform that lands the artwork squarely in the widget's rectangle.
    /// </summary>
    private static (double A, double B, double C, double D, double E, double F) MapToRect(
        PdfDictionary artwork, PdfRectangle rect)
    {
        var box = artwork.Elements.GetRectangle("/BBox");

        double[] matrix = [1, 0, 0, 1, 0, 0];
        if (artwork.Elements.GetArray("/Matrix") is { } supplied && supplied.Elements.Count >= 6)
        {
            for (var i = 0; i < 6; i++)
                matrix[i] = supplied.Elements.GetReal(i);
        }

        // The bounding box goes through the artwork's own matrix first, because that is what a
        // viewer applies before anything else, and its corners can end up anywhere.
        (double X, double Y) Apply(double x, double y) =>
            ((matrix[0] * x) + (matrix[2] * y) + matrix[4],
             (matrix[1] * x) + (matrix[3] * y) + matrix[5]);

        (double X, double Y)[] corners =
        [
            Apply(box.X1, box.Y1), Apply(box.X2, box.Y1),
            Apply(box.X1, box.Y2), Apply(box.X2, box.Y2),
        ];

        var left = corners.Min(corner => corner.X);
        var right = corners.Max(corner => corner.X);
        var bottom = corners.Min(corner => corner.Y);
        var top = corners.Max(corner => corner.Y);

        var width = right - left;
        var height = top - bottom;

        // Artwork with no extent cannot be scaled, only placed.
        var scaleX = width > 0 ? rect.Width / width : 1;
        var scaleY = height > 0 ? rect.Height / height : 1;

        return (scaleX, 0, 0, scaleY,
            Math.Min(rect.X1, rect.X2) - (left * scaleX),
            Math.Min(rect.Y1, rect.Y2) - (bottom * scaleY));
    }

    /// <summary>Puts the artwork in the page's resources under a name nothing else is using.</summary>
    private static string? Register(PdfPage page, PdfDictionary artwork)
    {
        var resources = page.Elements.GetDictionary("/Resources");
        if (resources is null)
        {
            resources = new PdfDictionary(page.Owner);
            page.Elements["/Resources"] = resources;
        }

        var xobjects = resources.Elements.GetDictionary("/XObject");
        if (xobjects is null)
        {
            xobjects = new PdfDictionary(page.Owner);
            resources.Elements["/XObject"] = xobjects;
        }

        // The name has to be one the template did not use, or registering would displace
        // something the page already draws.
        var index = 0;
        string name;
        do
        {
            name = $"/MpState{index++}";
        }
        while (xobjects.Elements.ContainsKey(name));

        xobjects.Elements[name] = artwork.Reference is { } reference ? reference : artwork;
        return name;
    }

    private static string Number(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
