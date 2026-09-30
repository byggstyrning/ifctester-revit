using System.Globalization;

namespace IfcTesterRevit.Writeback;

/// <summary>
/// Turns the text a user typed in the web app into a value for an integer or a yes/no
/// parameter. Lengths, areas and other doubles are not parsed here: they go through
/// Parameter.SetValueString so Revit reads them in the project's display units.
/// </summary>
public static class ValueParsing
{
    public static bool TryParseInteger(string? text, out int value)
    {
        return int.TryParse((text ?? "").Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseYesNo(string? text, out bool value)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "true":
            case "yes":
            case "1":
                value = true;
                return true;
            case "false":
            case "no":
            case "0":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }
}
