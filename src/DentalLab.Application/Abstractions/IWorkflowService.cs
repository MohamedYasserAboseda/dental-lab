using DentalLab.Application.Workflow;

namespace DentalLab.Application.Abstractions;

public interface IWorkflowService
{
    /// <summary>One column per production stage (Received to Ready) with the open cases, earliest due date first.</summary>
    Task<BoardData> GetBoardAsync(BoardQuery query, CancellationToken ct = default);
}
