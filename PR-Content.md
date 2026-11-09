# Code Review Issue Resolutions for RedStar.Agent

Resolves several code review points for the `RedStar.Agent` component.

## Changes:
- **`Program.cs` / `AgentApp.cs`**:
  - Removed code smell of using reflection to modify `ChatClientAgent` options. Modified factories to accept tool parameter natively.
  - Hardcoded tool construction replaced by explicitly registering `AITool` implementations into the Dependency Injection container.
  - Implemented missing cancellation token logic for the `session.SendAsync` chat loop via `CancellationTokenSource` and `Console.CancelKeyPress`.
- **`BashTool.cs`**:
  - Fixed naive command escaping logic by leveraging `StandardInput` for bash commands.
  - Mitigated process hanging risks by adding a 1-minute timeout `CancellationToken` mechanism.
- **`EditTool.cs`**:
  - Replaced global `Replace` operation with a localized first-occurrence `IndexOf` based substring replacement to prevent unintended data corruption.
- **`WriteTool.cs`**:
  - Removed over-simplified and misleading pseudo-diff slice logic output when rewriting files.