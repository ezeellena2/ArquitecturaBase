using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static partial class FormCatalogImportPolicy
{
    private static readonly string[] SupportedCultures = ["es", "en"];
    public static Result<FormCatalogImportPlan> Build(FormCatalogImportSource source, FormSpreadsheet sheet,
        FormCatalogImportMode mode, string culture)
    {
        if (mode is not (FormCatalogImportMode.Replace or FormCatalogImportMode.Append)) return FormCatalogErrors.ContentInvalid;
        if (sheet.Rows.Any(row => row.Cells.Count > FormCatalogImportLimits.MaximumColumns)) return FormCatalogImportErrors.TooManyColumns;
        if (sheet.Rows.Count == 0) return FormCatalogImportErrors.MissingHeader("code");
        var header = sheet.Rows[0];
        var fields = (source.Fields ?? []).Where(field => field.IsActive).ToArray();
        var resolvers = SourceResolvers(source);
        var sourceTexts = (source.Sources ?? new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>())
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(item => item.Id));
        var columns = new Dictionary<string,int>(StringComparer.Ordinal);
        var canonicalFields = new HashSet<string>(StringComparer.Ordinal);
        var issues = new List<FormCatalogImportIssue>();
        for (var index = 0; index < header.Cells.Count; index++)
        {
            var match = MatchHeader(header.Cells[index].Text ?? "", fields);
            if (match.Ambiguous) return FormCatalogImportErrors.DuplicateHeader(header.Cells[index].Text ?? "");
            var key = match.Key;
            if (key is null)
            {
                if (!string.IsNullOrWhiteSpace(header.Cells[index].Text))
                    issues.Add(Issue(header.Number,header.Cells[index].Text!,FormCatalogImportErrors.UnknownColumn,"Warning"));
            }
            else if (!columns.TryAdd(key,index)) return FormCatalogImportErrors.DuplicateHeader(key);
            else if (match.Canonical) canonicalFields.Add(key);
        }
        foreach (var required in new[] { "code", "text" })
            if (!columns.ContainsKey(required)) return FormCatalogImportErrors.MissingHeader(required);
        var valueHeader = Cell(header,columns,"value")?.Text ?? "";
        var canonicalValueColumn = IsCanonicalValueHeader(valueHeader);
        var invalidValueHeader = canonicalValueColumn && (source.ValueType == BusinessFormCatalogValueType.Text ||
            !SupportedCultures.Any(language => NormalizeHeader(valueHeader) ==
                NormalizeHeader(FormCatalogImportTemplate.ValueHeader(source.ValueType,language,true))));
        var rows = sheet.Rows.Skip(1).Where(row => row.Cells.Any(cell => cell.Error is not null || !string.IsNullOrWhiteSpace(cell.Text))).ToList();
        if (rows.Count > 0 && rows[0].Number == 2 && IsRuleRow(rows[0],columns,source.ValueType,fields)) rows.RemoveAt(0);
        if (rows.Count > BusinessFormCatalog.MaxItems) return FormCatalogImportErrors.TooManyRows;
        var current = source.Items.ToDictionary(item => item.Code,StringComparer.OrdinalIgnoreCase);
        var present = rows.Select(row => Cell(row,columns,"code")?.Text?.Trim()).Where(code => !string.IsNullOrEmpty(code)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicates = rows.Where(row => Cell(row,columns,"code")?.Error is null)
            .GroupBy(row => Cell(row,columns,"code")?.Text?.Trim() ?? "",StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .ToDictionary(group => group.Key,group => group.Select(row=>row.Number).ToArray(),StringComparer.OrdinalIgnoreCase);
        var valid = new List<FormCatalogImportParsedRow>();
        var errorRows = new HashSet<int>();
        foreach (var row in rows)
        {
            var start = issues.Count;
            var invalidCells = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (column,index) in columns)
                if (row.Cells.ElementAtOrDefault(index)?.Error is { } error)
                { issues.Add(Issue(row.Number,column,error)); invalidCells.Add(column); }
            var code = Cell(row,columns,"code")?.Text?.Trim() ?? "";
            var text = Cell(row,columns,"text")?.Text ?? "";
            current.TryGetValue(code,out var existing);
            var textEn = columns.ContainsKey("textEn") ? Optional(Cell(row,columns,"textEn")?.Text) : existing?.TextEn;
            var detail = columns.ContainsKey("detail") ? Optional(Cell(row,columns,"detail")?.Text) : existing?.DetailEs;
            var detailEn = columns.ContainsKey("detailEn") ? Optional(Cell(row,columns,"detailEn")?.Text) : existing?.DetailEn;
            Check("code",code,true,80,FormCatalogImportErrors.CodeTooLong);
            Check("text",text,true,BusinessFormCatalogItem.MaxTextLength,FormCatalogImportErrors.TextTooLong);
            Check("textEn",textEn,false,BusinessFormCatalogItem.MaxTextLength,FormCatalogImportErrors.TextTooLong);
            Check("detail",detail,false,BusinessFormCatalogItem.MaxDetailLength,FormCatalogImportErrors.DetailTooLong);
            Check("detailEn",detailEn,false,BusinessFormCatalogItem.MaxDetailLength,FormCatalogImportErrors.DetailTooLong);
            if (!invalidCells.Contains("code") && duplicates.TryGetValue(code,out var repeated))
                issues.Add(Issue(row.Number,"code",FormCatalogImportErrors.DuplicateCode,related:repeated.First(number=>number!=row.Number)));
            var activeText = NormalizeHeader(Cell(row,columns,"active")?.Text ?? "");
            var active = activeText is "" or "si" or "yes" or "true" or "1";
            if (!invalidCells.Contains("active") && !active && activeText is not ("no" or "false" or "0"))
                issues.Add(Issue(row.Number,"active",FormCatalogImportErrors.ActiveInvalid));
            var valueCell = Cell(row,columns,"value");
            Result<string?> value = invalidValueHeader ? FormCatalogErrors.ItemValueInvalid :
                FormCatalogValuePolicy.NormalizeImport(source.ValueType,valueCell?.Text,culture,
                    canonicalValueColumn || valueCell?.IsCanonicalValue == true);
            if (!invalidCells.Contains("value") && value.IsFailure) issues.Add(Issue(row.Number,"value",value.Error));
            var fieldValues = ParseFields(fields, resolvers, existing, row, columns, canonicalFields, invalidCells, active, culture, issues);
            if (issues.Count != start) errorRows.Add(row.Number);
            else valid.Add(new(row.Number,code,text,textEn,detail,detailEn,active,value.Value,fieldValues));

            void Check(string column,string? content,bool required,int maximum,Error lengthError)
            {
                if (invalidCells.Contains(column)) return;
                if (required && string.IsNullOrWhiteSpace(content)) issues.Add(Issue(row.Number,column,FormCatalogImportErrors.RequiredValue));
                else if (content is { Length: > 0 })
                {
                    if (content.Length > maximum) issues.Add(Issue(row.Number,column,lengthError,length:content.Length));
                    else if (!StorableText.IsStorable(content)) issues.Add(Issue(row.Number,column,FormCatalogErrors.ContentInvalid));
                }
            }
        }
        ValidateUniqueFields(source, fields, valid, mode, present, issues, errorRows);
        var newCount = valid.Count(row=>!current.ContainsKey(row.Code));
        if (source.Items.Count + newCount > BusinessFormCatalog.MaxItems) return FormCatalogErrors.TooManyItems;
        var mutations = new List<FormCatalogManualItemSaveRequest>();
        var added = new List<FormCatalogImportAddedRow>();
        var changed = new List<FormCatalogImportChangedRow>();
        var deactivated = new List<FormCatalogImportDeactivatedRow>();
        var reactivated = 0;
        var unchanged = 0;
        var final = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in source.Items) final.Add(item.Code,item.TextEs);
        foreach (var row in valid)
        {
            final[row.Code] = row.TextEs;
            if (!current.TryGetValue(row.Code,out var item))
            {
                mutations.Add(Input(row,null,fields)); added.Add(new(row.Number,row.Code,Text(row,culture),row.Value,
                    fields.Length == 0 ? null : row.Fields.Where(pair => fields.Any(field => field.Id.ToString("D") == pair.Key))
                        .ToDictionary(pair => pair.Key, pair => FormCatalogFieldPolicy.Input(pair.Value), StringComparer.Ordinal),
                    FieldTexts(row, fields, sourceTexts, culture)));
                continue;
            }
            var changes = Changes(item,row,fields,sourceTexts,culture);
            if (changes.Count == 0) unchanged++;
            else
            {
                mutations.Add(Input(row,item.Id,fields)); changed.Add(new(row.Number,item.Id,row.Code,changes));
                if (!item.IsActive && row.Active) reactivated++;
                if (item.IsActive && !row.Active) deactivated.Add(new(item.Id,row.Code,Text(row,culture)));
            }
        }
        if (mode == FormCatalogImportMode.Replace)
            foreach (var item in source.Items.Where(item=>item.IsActive && !present.Contains(item.Code)))
            {
                mutations.Add(new("item-"+item.Id.ToString("N"),item.Id,item.Code,
                    new Dictionary<string,string?> { ["es"]=item.TextEs,["en"]=item.TextEn },
                    new Dictionary<string,string?> { ["es"]=item.DetailEs,["en"]=item.DetailEn },false,item.Value));
                deactivated.Add(new(item.Id,item.Code,culture=="en"&&!string.IsNullOrWhiteSpace(item.TextEn)?item.TextEn:item.TextEs));
            }
        var textCodes = final.GroupBy(pair=>pair.Value,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group=>group.Key,group=>group.Select(pair=>pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
        var fileCodes = valid.Select(row=>row.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var textRows = valid.GroupBy(row=>row.TextEs,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group=>group.Key,group=>group.Select(row=>(int?)row.Number).Take(2).ToArray(),StringComparer.OrdinalIgnoreCase);
        var seenTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in valid)
        {
            var owners = textCodes[row.TextEs];
            if (owners.Count > 1 && (!seenTexts.Add(row.TextEs) || owners.Any(code=>!fileCodes.Contains(code))))
                issues.Add(Issue(row.Number,"text",FormCatalogImportErrors.DuplicateText,"Warning",
                    related:textRows[row.TextEs].FirstOrDefault(number=>number!=row.Number)));
        }
        var warnings = issues.Count(issue=>issue.Severity=="Warning");
        var counts = new FormCatalogImportCounts(added.Count,changed.Count,deactivated.Count,reactivated,unchanged,errorRows.Count,warnings);
        var confirmations = new FormCatalogImportConfirmations(deactivated.Count*4>source.Items.Count(item=>item.IsActive)?deactivated.Count:0,
            valid.Count==0,rows.Count>0&&errorRows.Count*10>rows.Count);
        var limit = FormCatalogImportLimits.MaximumPreviewRows;
        var preview = new FormCatalogImportPreviewResponse(source.Id,source.Revision,mode.ToString(),sheet.Sha256,sheet.Name,rows.Count,counts,
            issues.OrderBy(issue=>issue.Severity=="Error"?0:1).ThenBy(issue=>issue.Row).Take(limit).ToArray(),added.Take(limit).ToArray(),changed.Take(limit).ToArray(),deactivated.Take(limit).ToArray(),
            issues.Count>limit||added.Count>limit||changed.Count>limit||deactivated.Count>limit,confirmations,
            source.LastImportSha256==sheet.Sha256?source.LastImportAtUtc:null);
        return new FormCatalogImportPlan(preview,mutations);
    }

    public static Result ValidateConfirmations(FormCatalogImportPlan plan,FormCatalogImportRequest request)
    {
        if (!string.Equals(plan.Preview.SheetSha256,request.SheetSha256,StringComparison.Ordinal)) return FormCatalogImportErrors.SheetChanged;
        var expected = plan.Preview.Confirmations;
        return expected.EmptySheet&&!request.ConfirmEmpty || plan.Preview.Counts.ErrorRows>0&&!request.ConfirmErrors
            || expected.Deactivations>0&&request.ConfirmDeactivations!=expected.Deactivations
            ? FormCatalogImportErrors.ConfirmationRequired : Result.Success();
    }

    private static FormSpreadsheetCell? Cell(FormSpreadsheetRow row,IReadOnlyDictionary<string,int> columns,string key) =>
        columns.TryGetValue(key,out var position)?row.Cells.ElementAtOrDefault(position):null;
    private static string? Optional(string? text) => string.IsNullOrWhiteSpace(text)?null:text;
    private static string Text(FormCatalogImportParsedRow row,string culture) => culture=="en"&&!string.IsNullOrWhiteSpace(row.TextEn)?row.TextEn:row.TextEs;
    private static FormCatalogManualItemSaveRequest Input(FormCatalogImportParsedRow row,Guid? id,BusinessFormCatalogField[] fields) => new(id is null?"import-row"+row.Number:"item-"+id.Value.ToString("N"),id,row.Code,
        new Dictionary<string,string?> { ["es"]=row.TextEs,["en"]=row.TextEn },new Dictionary<string,string?> { ["es"]=row.DetailEs,["en"]=row.DetailEn },row.Active,row.Value,
        fields.Length == 0 ? null : row.Fields.Where(pair => fields.Any(field => field.Id.ToString("D") == pair.Key))
            .ToDictionary(pair => pair.Key, pair => FormCatalogFieldPolicy.Input(pair.Value), StringComparer.Ordinal));
    private static FormCatalogImportIssue Issue(int row,string column,Error error,string severity="Error",int? related=null,int? length=null) =>
        new(row,column,severity,error.Code,ErrorMessages.Get(error.Code),related,length);
    private static List<FormCatalogImportChange> Changes(FormCatalogStoredItemRow item,FormCatalogImportParsedRow row,BusinessFormCatalogField[] fields,
        Dictionary<Guid, Dictionary<Guid, FormCatalogStoredItemRow>> sourceTexts,string culture)
    {
        var changes = new List<FormCatalogImportChange>();
        Add("code",item.Code,row.Code); Add("text",item.TextEs,row.TextEs); Add("textEn",item.TextEn,row.TextEn);
        Add("detail",item.DetailEs,row.DetailEs); Add("detailEn",item.DetailEn,row.DetailEn);
        Add("active",item.IsActive?"true":"false",row.Active?"true":"false"); Add("value",item.Value,row.Value);
        var previous = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson);
        foreach (var field in fields)
        {
            var key = field.Id.ToString("D");
            var before = previous.TryGetValue(key, out var previousValue) ? FormCatalogFieldPolicy.Input(previousValue) : null;
            var after = row.Fields.TryGetValue(key, out var currentValue) ? FormCatalogFieldPolicy.Input(currentValue) : null;
            if (!string.Equals(before, after, StringComparison.Ordinal))
                changes.Add(new(key, before, after, SourceText(field, before, sourceTexts, culture), SourceText(field, after, sourceTexts, culture)));
        }
        return changes;
        void Add(string column,string? before,string? after) { if (!string.Equals(before,after,StringComparison.Ordinal)) changes.Add(new(column,before,after)); }
    }
    private static Dictionary<string, string>? FieldTexts(FormCatalogImportParsedRow row,BusinessFormCatalogField[] fields,
        Dictionary<Guid, Dictionary<Guid, FormCatalogStoredItemRow>> sourceTexts,string culture)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields.Where(field => field.Type == "catalog"))
        {
            var key = field.Id.ToString("D");
            var value = row.Fields.TryGetValue(key, out var stored) ? FormCatalogFieldPolicy.Input(stored) : null;
            if (SourceText(field, value, sourceTexts, culture) is { } text) texts.Add(key, text);
        }
        return texts.Count == 0 ? null : texts;
    }
    private static string? SourceText(BusinessFormCatalogField field,string? value,
        Dictionary<Guid, Dictionary<Guid, FormCatalogStoredItemRow>> sourceTexts,string culture)
    {
        if (field.Type != "catalog" || field.SourceCatalogId is not { } sourceId || !Guid.TryParse(value, out var id)
            || !sourceTexts.TryGetValue(sourceId, out var source) || !source.TryGetValue(id, out var item)) return null;
        return culture == "en" && !string.IsNullOrWhiteSpace(item.TextEn) ? item.TextEn : item.TextEs;
    }
    private static bool IsRuleRow(FormSpreadsheetRow row,IReadOnlyDictionary<string,int> columns,BusinessFormCatalogValueType type,
        BusinessFormCatalogField[] fields)
    {
        if (!string.IsNullOrEmpty(Cell(row,columns,"code")?.Text) || row.Cells.Any(cell=>cell.Error is not null)) return false;
        foreach (var culture in new[]{"es","en"})
        {
            var expected = FormCatalogImportTemplate.Columns(type,culture,fields:fields).ToDictionary(column=>column.Key,column=>column.Rule,StringComparer.Ordinal);
            if (columns.All(pair=>expected.TryGetValue(pair.Key,out var rule)&&string.Equals(row.Cells.ElementAtOrDefault(pair.Value)?.Text??"",rule,StringComparison.Ordinal))
                && row.Cells.Select((cell,index)=>(cell,index)).Where(pair=>!columns.Values.Contains(pair.index)).All(pair=>string.IsNullOrEmpty(pair.cell.Text))) return true;
        }
        return false;
    }
    private static bool IsCanonicalValueHeader(string text) => FormCatalogImportTemplate.IsCanonicalValueHeader(text);
    private static string NormalizeHeader(string text) => FormCatalogImportTemplate.NormalizeHeader(text);
}
