namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence;
internal static class FormAdvisoryLockKeys
{
    public static readonly string MissingRequests = "forms:missing-requests";
    public static readonly string Definitions = "forms:definitions";
    public static string Request(Guid actor, Guid requestId) => $"forms:request:{actor:N}:{requestId:N}";
    public static string Catalog(Guid catalogId) => $"forms:catalog:{catalogId:N}";
    public static string Capture(Guid sourceReceiptId) => $"forms:capture:{sourceReceiptId:N}";
}
