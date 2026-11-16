using System.ComponentModel.DataAnnotations;
namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence;
public sealed class AiRateLimitOptions
{
    [Range(1, 100000)] public int ReadPermitLimit { get; set; } = 120;
    [Range(1, 100000)] public int WritePermitLimit { get; set; } = 20;
    [Range(1, 100000)] public int TestPermitLimit { get; set; } = 4;
    [Range(1, 100000)] public int PlacesPermitLimit { get; set; } = 60;
    [Range(1, 100000)] public int AssistantSubmitPermitLimit { get; set; } = 6;
    // I9: the uploads of Home (file-turns, audio and documents) that run at once in this copy, besides one per person, and how many wait
    // for a free one; with the queue full the next one answers 429. A pilot figure, as the others.
    [Range(1, 100000)] public int UploadConcurrencyLimit { get; set; } = 4;
    [Range(0, 100000)] public int UploadQueueLimit { get; set; } = 8;
    // I9: with its slots taken, the seconds an upload has to send its whole body; past them it is cut and the slots go back.
    [Range(1, 3600)] public int UploadBodySeconds { get; set; } = 120;
}
