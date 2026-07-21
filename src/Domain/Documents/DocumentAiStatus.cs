namespace Domain.Documents;

public enum DocumentAiStatus
{
    None = 0,
    Queued = 1,
    Processing = 2,
    Passed = 3,
    Failed = 4,
    Error = 5
}
