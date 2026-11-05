using BaryoDev.Umbraco.MediumImport.Engine;

namespace BaryoDev.Umbraco.MediumImport.Jobs;

public sealed record ImportJobRequest(Guid UploadId, IReadOnlyCollection<string> PostIds, ImportSettings Settings, int UserId);
