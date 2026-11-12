using ArquitecturaBase.Application.Interfaces.Channels;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// The use case behind the inbox's port for channels (<see cref="IStructuredDocumentChannel"/>): it registers, prepares and leaves
/// unclassified in a unit of work of its own, so the port is a service, as the sinks are.
/// </summary>
public interface IFormReceivedDocumentChannelService : IStructuredDocumentChannel;
