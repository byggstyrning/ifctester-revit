namespace IfcTesterRevit.Writeback;

/// <summary>
/// The 22-character IFC GlobalId form of a GUID (IFC "compressed" base 64).
/// No Revit types, so it can be exercised outside Revit.
/// </summary>
public static class IfcGuid
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>
    /// Compresses a GUID to its IFC GlobalId. The byte order is the one the Revit IFC exporter
    /// uses on the value of ExportUtils.GetExportId (Revit.IFC.Export GUIDUtil.ConvertToIFCGuid).
    /// </summary>
    public static string FromGuid(Guid guid)
    {
        var b = guid.ToByteArray();
        var groups = new ulong[]
        {
            b[3],
            (ulong)(b[2] * 65536 + b[1] * 256 + b[0]),
            (ulong)(b[5] * 65536 + b[4] * 256 + b[7]),
            (ulong)(b[6] * 65536 + b[8] * 256 + b[9]),
            (ulong)(b[10] * 65536 + b[11] * 256 + b[12]),
            (ulong)(b[13] * 65536 + b[14] * 256 + b[15])
        };

        var chars = new char[22];
        var offset = 0;
        for (var i = 0; i < groups.Length; i++)
        {
            var length = i == 0 ? 2 : 4;
            for (var j = 0; j < length; j++)
            {
                chars[offset + length - j - 1] = Alphabet[(int)(groups[i] % 64)];
                groups[i] /= 64;
            }
            offset += length;
        }

        return new string(chars);
    }

    /// <summary>
    /// True when the string has the shape of an IFC GlobalId. The exporter ignores a stored
    /// IfcGUID parameter that fails this test and falls back to the derived id.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (value == null || value.Length != 22) return false;
        if (value[0] < '0' || value[0] > '3') return false;
        foreach (var c in value)
        {
            if (Alphabet.IndexOf(c) < 0) return false;
        }
        return true;
    }
}
