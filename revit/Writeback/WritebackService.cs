using Autodesk.Revit.DB;

namespace IfcTesterRevit.Writeback;

/// <summary>
/// The two write-back operations. Both take the document directly and must run on the Revit
/// thread; nothing here knows about HTTP.
/// </summary>
public static class WritebackService
{
    public const string TransactionName = "IfcTester: apply IDS fixes";

    /// <summary>
    /// For each failed property or attribute: the element with that GlobalId and the parameters
    /// a fix could be written to. Reads only.
    /// </summary>
    public static ResolveResponse Resolve(Document? document, ResolveRequest request, ExportMapping mapping)
    {
        var response = new ResolveResponse
        {
            Configuration = mapping.Configuration,
            MappingFiles = mapping.Files.ToList(),
            MappingNote = mapping.Note
        };

        var items = request.Items ?? new List<ResolveItem>();
        if (document == null)
        {
            response.Items = items
                .Select(i => new ResolveResult { Key = i.Key, Message = "No model is open in Revit." })
                .ToList();
            return response;
        }

        var index = GlobalIdIndex.Build(document);
        var resolver = new ParameterResolver();

        foreach (var item in items)
        {
            var result = new ResolveResult { Key = item.Key };
            response.Items.Add(result);

            try
            {
                var element = index.Find(item.GlobalId, out var message);
                if (element == null)
                {
                    result.Message = message;
                    continue;
                }

                var type = TypeOf(element);
                result.Found = true;
                result.ElementId = element.Id.Value;
                result.ElementName = DisplayName(element, type);
                result.Category = element.Category?.Name;
                if (type != null)
                {
                    result.TypeName = type.Name;
                    result.TypeInstanceCount = index.InstanceCount(type.Id);
                }

                var name = (item.Name ?? "").Trim();
                var facet = (item.Facet ?? "").Trim().ToLowerInvariant();
                if (name.Length == 0)
                {
                    result.Message = "The request names no property or attribute.";
                }
                else if (facet == "property")
                {
                    result.Candidates = resolver.ForProperty(element, type, item.PropertySet, name, mapping);
                    if (result.Candidates.Count == 0)
                    {
                        var qualified = string.IsNullOrWhiteSpace(item.PropertySet) ? "" : $" or '{item.PropertySet!.Trim()}.{name}'";
                        result.Message = $"No parameter named '{name}'{qualified} on the element or its type. " +
                                         "Add the parameter in Revit, or map the property to an existing parameter in the export setup's property set file.";
                    }
                }
                else if (facet == "attribute")
                {
                    if (!ParameterResolver.IsSupportedAttribute(name))
                    {
                        result.Message = $"The attribute '{name}' cannot be set from here. Name, Description, ObjectType and LongName can.";
                    }
                    else
                    {
                        result.Candidates = resolver.ForAttribute(element, type, name, mapping);
                        if (result.Candidates.Count == 0)
                        {
                            result.Message = $"The element has no 'Ifc{name}' parameter to override the attribute with. " +
                                             "Add the IFC shared parameter to its category in Revit first.";
                        }
                    }
                }
                else
                {
                    result.Message = $"Facet '{item.Facet}' cannot be fixed from here. Property and attribute facets can.";
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Revit could not read the element: {ex.Message}";
            }
        }

        return response;
    }

    /// <summary>
    /// Writes the values inside one transaction, so one Ctrl+Z in Revit undoes the whole batch.
    /// A change that cannot be written is reported and the others still apply. When nothing
    /// applies the transaction is rolled back and no undo entry is left.
    /// </summary>
    public static ApplyResponse Apply(Document? document, ApplyRequest request)
    {
        var changes = request.Changes ?? new List<ChangeItem>();
        var results = changes.Select(c => new ChangeResult { Key = c.Key }).ToList();

        var blocked = document == null ? "No model is open in Revit."
            : document.IsReadOnly ? $"'{document.Title}' is read-only in Revit."
            : document.IsModifiable ? "Revit is in the middle of another change. Finish it, then apply again."
            : null;

        if (blocked != null || changes.Count == 0)
        {
            foreach (var result in results) result.Message = blocked;
            return Summarize(results);
        }

        var index = GlobalIdIndex.Build(document!);
        var resolver = new ParameterResolver();
        // What this batch has already written: (element or type, parameter) -> the text written.
        var written = new Dictionary<(ElementId Target, ElementId Parameter), string>();

        using var transaction = new Transaction(document, TransactionName);
        try
        {
            transaction.Start();

            for (var i = 0; i < changes.Count; i++)
            {
                try
                {
                    ApplyOne(document!, index, resolver, written, changes[i], results[i]);
                }
                catch (Exception ex)
                {
                    results[i].Ok = false;
                    results[i].NewValue = null;
                    results[i].Message = $"Revit refused the change: {ex.Message}";
                }
            }

            if (results.Any(r => r.Ok))
            {
                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    FailApplied(results, "Revit did not keep the changes: the transaction was cancelled or rejected in Revit. Nothing was written.");
                }
            }
            else
            {
                transaction.RollBack();
            }
        }
        catch (Exception ex)
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
            {
                transaction.RollBack();
            }
            FailApplied(results, $"Revit could not commit the changes: {ex.Message} Nothing was written.");
            foreach (var result in results.Where(r => r.Message == null))
            {
                result.Message = $"Revit could not start the change: {ex.Message}";
            }
        }

        return Summarize(results);
    }

    private static void ApplyOne(
        Document document,
        GlobalIdIndex index,
        ParameterResolver resolver,
        Dictionary<(ElementId Target, ElementId Parameter), string> written,
        ChangeItem change,
        ChangeResult result)
    {
        var element = index.Find(change.GlobalId, out var message);
        if (element == null)
        {
            result.Message = message;
            return;
        }

        var scope = (change.Scope ?? "").Trim().ToLowerInvariant();
        if (scope != WritebackScope.Instance && scope != WritebackScope.Type)
        {
            result.Message = $"Unknown scope '{change.Scope}'. Expected 'instance' or 'type'.";
            return;
        }

        var isType = scope == WritebackScope.Type;
        var type = TypeOf(element);
        var target = isType ? type : element;
        var elementName = DisplayName(element, type);
        if (target == null)
        {
            result.Message = $"'{elementName}' has no type to write a type parameter to.";
            return;
        }

        var parameterName = change.Parameter ?? "";
        var parameter = resolver.FindForWrite(target, parameterName);
        var where = isType ? $"the type of '{elementName}'" : $"'{elementName}'";
        if (parameter == null)
        {
            result.Message = $"Parameter '{parameterName}' no longer exists on {where}.";
            return;
        }
        if (parameter.StorageType == StorageType.ElementId || parameter.StorageType == StorageType.None)
        {
            result.Message = $"Parameter '{parameterName}' refers to another Revit element and cannot be set from a typed value.";
            return;
        }
        if (parameter.IsReadOnly)
        {
            result.Message = $"Parameter '{parameterName}' on {where} is read-only in Revit.";
            return;
        }

        var ownership = OwnershipProblem(document, target, where);
        if (ownership != null)
        {
            result.Message = ownership;
            return;
        }

        var text = change.ValueText;
        var typeNote = isType
            ? $"Type parameter: the value applies to all {index.InstanceCount(target.Id)} instances of type '{target.Name}'."
            : null;

        if (written.TryGetValue((target.Id, parameter.Id), out var earlier))
        {
            if (!string.Equals(earlier, text, StringComparison.Ordinal))
            {
                result.Message = isType
                    ? $"Another change in this batch sets '{parameterName}' of type '{target.Name}' to '{earlier}'. A type parameter has one value for all its instances."
                    : $"Another change in this batch sets '{parameterName}' on {where} to '{earlier}'.";
                return;
            }

            result.Ok = true;
            result.NewValue = ParameterResolver.DisplayValue(parameter);
            result.Message = typeNote;
            return;
        }

        using var step = new SubTransaction(document);
        step.Start();
        string? problem;
        try
        {
            problem = SetValue(parameter, text);
        }
        catch
        {
            step.RollBack();
            throw;
        }
        if (problem != null)
        {
            step.RollBack();
            result.Message = problem;
            return;
        }
        step.Commit();

        written[(target.Id, parameter.Id)] = text;
        result.Ok = true;
        result.NewValue = ParameterResolver.DisplayValue(parameter);
        result.Message = typeNote;
    }

    /// <summary>Sets the value; returns null on success and otherwise what the user should change.</summary>
    private static string? SetValue(Parameter parameter, string text)
    {
        var name = parameter.Definition.Name;
        switch (parameter.StorageType)
        {
            case StorageType.String:
                return parameter.Set(text) ? null : $"Revit did not accept '{text}' for '{name}'.";

            case StorageType.Integer:
                if (ParameterResolver.IsYesNo(parameter))
                {
                    if (!ValueParsing.TryParseYesNo(text, out var flag))
                    {
                        return $"'{name}' is a yes/no parameter: '{text}' is not one of true, false, yes, no, 1, 0.";
                    }
                    return parameter.Set(flag ? 1 : 0) ? null : $"Revit did not accept '{text}' for '{name}'.";
                }

                if (!ValueParsing.TryParseInteger(text, out var number))
                {
                    return $"'{name}' is an integer parameter: '{text}' is not a whole number.";
                }
                return parameter.Set(number) ? null : $"Revit did not accept '{text}' for '{name}'.";

            case StorageType.Double:
                // Revit reads the text in the project's display units, the way the Properties palette does.
                return parameter.SetValueString(text)
                    ? null
                    : $"Revit could not read '{text}' as a value for '{name}'. Type it the way the Properties palette shows it, for example '{parameter.AsValueString()}'.";

            default:
                return $"Parameter '{name}' cannot be set from a typed value.";
        }
    }

    /// <summary>In a workshared model: why this element cannot be edited right now, or null.</summary>
    private static string? OwnershipProblem(Document document, Element target, string where)
    {
        if (!document.IsWorkshared) return null;

        var status = WorksharingUtils.GetCheckoutStatus(document, target.Id, out var owner);
        if (status == CheckoutStatus.OwnedByOtherUser)
        {
            return $"{Capitalize(where)} is being edited by {owner}. Ask them to synchronize and relinquish it, then apply again.";
        }

        var updates = WorksharingUtils.GetModelUpdatesStatus(document, target.Id);
        if (updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral)
        {
            return $"{Capitalize(where)} has changed in the central model. Reload Latest in Revit, then apply again.";
        }

        return null;
    }

    private static Element? TypeOf(Element element)
    {
        var typeId = element.GetTypeId();
        return typeId == ElementId.InvalidElementId ? null : element.Document.GetElement(typeId);
    }

    /// <summary>"Basic Wall: Generic - 200mm" for an element with a type, the element name otherwise.</summary>
    private static string DisplayName(Element element, Element? type)
    {
        if (type is ElementType elementType)
        {
            var family = elementType.FamilyName;
            return string.IsNullOrEmpty(family) ? elementType.Name : $"{family}: {elementType.Name}";
        }
        return element.Name ?? "";
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    private static void FailApplied(List<ChangeResult> results, string message)
    {
        foreach (var result in results.Where(r => r.Ok))
        {
            result.Ok = false;
            result.NewValue = null;
            result.Message = message;
        }
    }

    private static ApplyResponse Summarize(List<ChangeResult> results)
    {
        return new ApplyResponse
        {
            Applied = results.Count(r => r.Ok),
            Failed = results.Count(r => !r.Ok),
            Results = results
        };
    }
}
