using ArquitecturaBase.Application.Interfaces.Channels;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// The use case behind the inbox's port for the rows of a spreadsheet (<see cref="IStructuredDocumentRowsChannel"/>, VC1-A): it registers them
/// in a unit of work of its own, so the port is a service, as the documents' one.
/// </summary>
public interface IFormReceivedDocumentRowService : IStructuredDocumentRowsChannel;
