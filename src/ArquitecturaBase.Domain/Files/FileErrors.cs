using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Files;

public static class FileErrors
{
    public static readonly Error HashMismatch = Error.Validation("Files.Content.HashMismatch", "The file hash does not match.");
    public static readonly Error InvalidType = Error.Validation("Files.Content.InvalidType", "The actual file type is not allowed or does not match.");
    public static readonly Error TooLarge = Error.Validation("Files.Content.TooLarge", "The file or response exceeds the size limit.");
    public static readonly Error InvalidMetadata = Error.Validation("Files.Content.InvalidMetadata", "The file metadata is invalid.");
    // N22: neutral, so that no module's words reach the core. A module that downloads its files names its own failure.
    public static readonly Error StorageUnavailable = Error.Validation("Files.Storage.Unavailable", "The private file storage is not available or did not keep the file.");
    public static readonly Error NotFound = Error.NotFound("Files.Content.NotFound", "The file does not belong to this response.");
    public static readonly Error Missing = Error.NotFound("Files.Content.Missing", "The stored copy of the file is missing.");
    public static Error AtField(Error error, Guid? fieldId) => fieldId is null ? error
        : error with { Metadata = new Dictionary<string, object?> { ["fieldId"] = fieldId.Value } };
}
