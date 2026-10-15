using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Files;

public static class FileErrors
{
    public static readonly Error HashMismatch = Error.Validation("Files.Content.HashMismatch", "The file hash does not match.");
    public static readonly Error InvalidType = Error.Validation("Files.Content.InvalidType", "The actual file type is not allowed or does not match.");
    public static readonly Error TooLarge = Error.Validation("Files.Content.TooLarge", "The file or response exceeds the size limit.");
    public static readonly Error InvalidMetadata = Error.Validation("Files.Content.InvalidMetadata", "The file metadata is invalid.");
    public static readonly Error Unavailable = Error.Validation("Files.Content.Unavailable", "The file could not be downloaded.");
    public static readonly Error NotFound = Error.NotFound("Files.Content.NotFound", "The file does not belong to this response.");
}
