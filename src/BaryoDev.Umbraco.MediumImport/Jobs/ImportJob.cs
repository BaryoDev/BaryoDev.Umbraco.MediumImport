using BaryoDev.Umbraco.MediumImport.Engine;

namespace BaryoDev.Umbraco.MediumImport.Jobs;

public sealed class ImportJob
{
    private int _state;
    private ImportProgress _progress = new(0, 0, null);
    private ImportResult? _result;
    private string? _error;

    internal ImportJob(Guid id, ImportJobRequest request)
    {
        Id = id;
        Request = request;
        Cts = new CancellationTokenSource();
    }

    public Guid Id { get; }

    public JobState State
    {
        get => (JobState)Volatile.Read(ref _state);
        internal set => Volatile.Write(ref _state, (int)value);
    }

    public ImportProgress Progress
    {
        get => Volatile.Read(ref _progress);
        internal set => Volatile.Write(ref _progress, value);
    }

    public ImportResult? Result
    {
        get => Volatile.Read(ref _result);
        internal set => Volatile.Write(ref _result, value);
    }

    public string? Error
    {
        get => Volatile.Read(ref _error);
        internal set => Volatile.Write(ref _error, value);
    }

    internal ImportJobRequest Request { get; }

    internal CancellationTokenSource Cts { get; }

    internal bool IsFinished => State is JobState.Completed or JobState.Failed or JobState.Cancelled;
}
