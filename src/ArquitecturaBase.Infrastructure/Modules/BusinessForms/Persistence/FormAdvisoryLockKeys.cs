namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence;
internal static class FormAdvisoryLockKeys
{
    public static readonly string Definitions = "forms:definitions";
    public static string Request(Guid actor, Guid requestId) => $"forms:request:{actor:N}:{requestId:N}";
}
