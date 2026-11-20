using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

/// <summary>
/// The enumerations of Forms saved as text. While two versions run side by side, the older one fails reading a value it does not know
/// ([migration guide](docs/guides/migracion.md#convivencia-de-versiones)), so a new value is first read by one deployment and written by the next.
/// Adding one means editing this list, and that edit is the moment to check the rule.
/// </summary>
public sealed class FormStoredEnumTests
{
    /// <summary>
    /// Enumeration, then its values in declaration order. `FormActionRunState.NotApplicable` is a known exception: the 2026-11-15 version started
    /// writing it in the same deployment that reads it, so returning to the previous image while rows with it exist makes its action reads fail.
    /// </summary>
    private static readonly Dictionary<string, string> Stored = new()
    {
        ["BusinessFormCatalogKind"] = "Manual,Filters,External",
        ["BusinessFormCatalogProposalState"] = "Pending,Approved,Discarded",
        ["BusinessFormCatalogValueType"] = "Text,Integer,Decimal,Date",
        ["BusinessFormIdentityKind"] = "Field,Choice",
        ["BusinessFormMissingTopicState"] = "Pending,Discarded",
        ["BusinessFormPersonAttributeType"] = "Catalog,Text",
        ["FormActionRunState"] = "Waiting,Running,Completed,Failed,Uncertain,NotApplicable",
        ["FormFolderItemKind"] = "Form,Catalog",
        ["FormPublicationState"] = "Accepted,Preparing,Publishing,Uncertain,ActivationPending,Published,Failed",
        ["FormReceivedDocumentEventKind"] = "Received,Discarded,Reopened,ExtractionRequested,FormChanged,ExtractionFailed,Confirmed,ChoseOther,FormChosen,Sent,OverLimit",
        ["FormReceivedDocumentNotSentReason"] = "NotSendable,OverLimit",
        ["FormReceivedDocumentRowKind"] = "Load,NotSendable,OverLimit,Repeated,Unmatched,Unseen",
        ["FormReceivedDocumentState"] = "Proposed,Unclassified,Confirmed,Discarded",
        ["FormRecordState"] = "Received,PendingApproval,Approved,Rejected,Voided",
        ["FormTaskState"] = "Open,Approved,Rejected,Closed",
    };

    [Fact]
    public void Enumerations_saved_as_text_match_the_versioned_list()
    {
        var actual = StoredEnums().ToDictionary(type => type.Name, type => string.Join(",", Enum.GetNames(type)));
        Assert.Equal(Stored.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value), actual.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value));
    }

    private static List<Type> StoredEnums()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql();
        options.UseOpenIddict<Guid>();
        using var db = new ApplicationDbContext(options.Options);
        return db.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(entity => entity.ClrType.Namespace?.StartsWith("ArquitecturaBase.Domain.Modules.BusinessForms", StringComparison.Ordinal) == true)
            .SelectMany(entity => entity.GetProperties())
            .Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).IsEnum && property.GetProviderClrType() == typeof(string))
            .Select(property => Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).Distinct().ToList();
    }
}
