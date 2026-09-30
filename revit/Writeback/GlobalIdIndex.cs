using Autodesk.Revit.DB;

namespace IfcTesterRevit.Writeback;

/// <summary>
/// IFC GlobalId to element, built in one pass over the document so a batch of lookups costs one
/// scan instead of one scan each.
///
/// The exporter gives an element the GlobalId stored in its IfcGUID parameter when that holds a
/// valid id, and otherwise the id derived from ExportUtils.GetExportId. Both are indexed. A type
/// is never a target: it is reached through one of its instances. Type ids are kept only to say
/// so when a failed entity turns out to be a type.
/// </summary>
public sealed class GlobalIdIndex
{
    private readonly Document _document;
    private readonly Dictionary<string, List<ElementId>> _stored = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementId> _derived = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementId> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<ElementId, int> _instancesPerType = new();

    private GlobalIdIndex(Document document)
    {
        _document = document;
    }

    public static GlobalIdIndex Build(Document document)
    {
        var index = new GlobalIdIndex(document);

        using var collector = new FilteredElementCollector(document);
        foreach (var element in collector.WhereElementIsNotElementType())
        {
            try
            {
                var id = element.Id;

                var stored = element.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString();
                if (IfcGuid.IsValid(stored))
                {
                    if (!index._stored.TryGetValue(stored!, out var owners))
                    {
                        owners = new List<ElementId>();
                        index._stored[stored!] = owners;
                    }
                    owners.Add(id);
                }

                index._derived[IfcGuid.FromGuid(ExportUtils.GetExportId(document, id))] = id;

                var typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    index._instancesPerType.TryGetValue(typeId, out var count);
                    index._instancesPerType[typeId] = count + 1;
                }
            }
            catch
            {
                // An element that cannot be read cannot be written to either; leave it out.
            }
        }

        using var typeCollector = new FilteredElementCollector(document);
        foreach (var type in typeCollector.WhereElementIsElementType())
        {
            try
            {
                var stored = type.get_Parameter(BuiltInParameter.IFC_TYPE_GUID)?.AsString();
                if (IfcGuid.IsValid(stored)) index._types[stored!] = type.Id;
                index._types[IfcGuid.FromGuid(ExportUtils.GetExportId(document, type.Id))] = type.Id;
            }
            catch
            {
                // Only used for a clearer message; an unreadable type is simply not recognised.
            }
        }

        return index;
    }

    /// <summary>
    /// The element exported with this GlobalId, or null with a message a user can act on.
    /// </summary>
    public Element? Find(string? globalId, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(globalId))
        {
            message = "The failed element has no GlobalId.";
            return null;
        }

        if (_stored.TryGetValue(globalId!, out var owners))
        {
            if (owners.Count == 1)
            {
                return _document.GetElement(owners[0]);
            }

            // Copies of an element keep the stored IfcGUID of the original. The exporter lets the
            // first one it meets keep the id and gives the others new ones, so which of them this
            // GlobalId belongs to cannot be told from here.
            message = $"{owners.Count} elements share the stored IfcGUID {globalId} " +
                      $"(element ids {string.Join(", ", owners.Select(o => o.Value))}), so the target is ambiguous. " +
                      "Clear the IfcGUID parameter on the copies and export again.";
            return null;
        }

        if (_derived.TryGetValue(globalId!, out var derivedOwner))
        {
            return _document.GetElement(derivedOwner);
        }

        if (_types.TryGetValue(globalId!, out var typeId))
        {
            var count = InstanceCount(typeId);
            message = $"This GlobalId is the type '{_document.GetElement(typeId)?.Name}', not an element. " +
                      (count > 0
                          ? $"Fix the failure on one of its {count} instances, or edit the type in Revit."
                          : "Edit the type in Revit.");
            return null;
        }

        message = $"No element in '{_document.Title}' has the GlobalId {globalId}. " +
                  "The audited IFC may come from another model or an older export, " +
                  "or the entity has no Revit element of its own (an opening, a stair run or landing, " +
                  "a part of a split wall, an element in a linked model).";
        return null;
    }

    /// <summary>How many elements are of this type.</summary>
    public int InstanceCount(ElementId typeId)
    {
        return _instancesPerType.TryGetValue(typeId, out var count) ? count : 0;
    }
}
