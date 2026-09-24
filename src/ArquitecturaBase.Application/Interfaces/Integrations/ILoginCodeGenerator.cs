namespace ArquitecturaBase.Application.Interfaces.Integrations;

public interface ILoginCodeGenerator
{
    /// <summary>Un código numérico aleatorio con la cantidad de dígitos configurada.</summary>
    string Generate();
}
