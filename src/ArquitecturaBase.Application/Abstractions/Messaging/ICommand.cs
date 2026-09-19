namespace ArquitecturaBase.Application.Abstractions.Messaging;

/// <summary>Comando que modifica estado y no devuelve valor.</summary>
public interface ICommand;

/// <summary>Comando que modifica estado y devuelve <typeparamref name="TResponse"/>.</summary>
public interface ICommand<TResponse>;
