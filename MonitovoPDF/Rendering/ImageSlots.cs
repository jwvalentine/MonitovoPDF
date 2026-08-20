using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace MonitovoPDF.Rendering;

/// <summary>One image placeholder on a page: its ordinal, its resource name, and its object.</summary>
internal sealed record ImageSlot(int Index, string ResourceName, PdfDictionary Image)
{
    public int PixelWidth => Image.Elements.GetInteger("/Width");

    public int PixelHeight => Image.Elements.GetInteger("/Height");
}

/// <summary>
/// Finds and replaces the image XObjects a page draws, for templates whose placeholders are
/// images rather than form fields.
/// </summary>
internal static class ImageSlots
{
    /// <summary>
    /// Lists a page's image placeholders in a defined order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PDF resource dictionary has no defined key order, so an ordinal only means something if
    /// the ordering rule is stated and never varies. This sorts by resource name, comparing runs
    /// of digits as numbers, so <c>/Im2</c> precedes <c>/Im10</c> rather than following it.
    /// </para>
    /// <para>
    /// Getting this wrong does not fail loudly: it silently swaps the content of one placeholder
    /// with another, and the document still renders.
    /// </para>
    /// </remarks>
    public static List<ImageSlot> On(PdfPage page)
    {
        var xobjects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        if (xobjects is null)
            return [];

        var found = new List<(string Name, PdfDictionary Image)>();

        foreach (var key in xobjects.Elements.Keys)
        {
            var image = Resolve(xobjects.Elements[key]);

            if (image is not null && image.Elements.GetName("/Subtype") == "/Image")
                found.Add((key, image));
        }

        found.Sort((left, right) => NaturalOrder.Compare(left.Name, right.Name));

        return [.. found.Select((entry, position) =>
            new ImageSlot(position + 1, entry.Name, entry.Image))];
    }

    /// <summary>
    /// Points a placeholder's resource name at <paramref name="replacement"/>, leaving the page's
    /// content stream alone.
    /// </summary>
    /// <remarks>
    /// The drawing operator and the transform that positions it are untouched, so whatever is put
    /// here lands exactly where the placeholder did, at exactly its size. A replacement whose
    /// proportions differ from the placeholder's is stretched to fit, which is the behaviour of
    /// the tools these templates were authored for.
    /// </remarks>
    public static void Replace(PdfPage page, ImageSlot slot, PdfDictionary replacement)
    {
        var xobjects = page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;

        xobjects.Elements[slot.ResourceName] = replacement.Reference is { } reference
            ? reference
            : replacement;
    }

    private static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfReference { Value: PdfDictionary dictionary } => dictionary,
        PdfDictionary dictionary => dictionary,
        _ => null
    };
}

/// <summary>Compares names so that embedded numbers sort by value rather than by character.</summary>
internal static class NaturalOrder
{
    public static int Compare(string left, string right)
    {
        int i = 0, j = 0;

        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                var start = i;
                var otherStart = j;

                while (i < left.Length && char.IsDigit(left[i])) i++;
                while (j < right.Length && char.IsDigit(right[j])) j++;

                // Compare the digit runs as numbers, ignoring leading zeros.
                var leftRun = left.AsSpan(start, i - start).TrimStart('0');
                var rightRun = right.AsSpan(otherStart, j - otherStart).TrimStart('0');

                if (leftRun.Length != rightRun.Length)
                    return leftRun.Length - rightRun.Length;

                var run = leftRun.SequenceCompareTo(rightRun);
                if (run != 0)
                    return run;

                continue;
            }

            var character = left[i].CompareTo(right[j]);
            if (character != 0)
                return character;

            i++;
            j++;
        }

        return (left.Length - i) - (right.Length - j);
    }
}

/// <summary>What to put in an image placeholder: a picture, or a barcode drawn as vectors.</summary>
internal sealed record ImageSlotContent(byte[]? Image, BarcodeContent? Barcode);
