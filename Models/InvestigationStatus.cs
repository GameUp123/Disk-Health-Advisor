namespace DiskHealthAdvisor.Models;

public enum InvestigationStatus
{
    New,
    WaitingForUserAction,
    WaitingForRecheck,
    Improved,
    NotChanged,
    Worse,
    PhysicalFailureSuspected,
    NeedMoreData,
    ClosedByUser
}
