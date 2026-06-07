You are working inside {{targetPath}}.

Build or continue iterating on a production-quality TODO list application based on this <USER-REQUEST>:

<USER-REQUEST>
{{userPrompt}}
</USER-REQUEST>

Required implementation constraints:
- Use ASP.NET Core MVC on .NET 10 for the web application.
- Use Bootstrap CSS for the UI styling.
- Use a traditional server-rendered HTTP GET/POST application model rather than React or a client SPA.
- Create or update the solution in the current working directory.
- The app must support adding, editing, completing, filtering, and deleting TODO items.
- Implement the UI with MVC controllers, Razor views, view models where helpful, and standard form posts.
- Include automated unit tests for the core application logic.
- The workflow will require at least {{minimumCodeCoveragePercent}}% unit test code coverage.
- Prefer clean, maintainable project structure and clear README/comments only when useful.

Current workspace assessment file:
{{workspaceStatusFile}}

If the file exists, read it and use it to understand the current state before making changes.
Current workspace summary: {{workspaceSummary}}
Existing app detected: {{appExists}}

When prior validation steps have already run, use these reports to decide what to fix next:
- dotnet build report: {{buildReportFile}}
- dotnet test report: {{testReportFile}}
- coverage report: {{coverageReportFile}}

Instructions:
1. Inspect the current codebase state.
2. If the application is missing, scaffold the full solution and projects needed.
3. If build/test/coverage reports are present, fix the issues they describe instead of starting over.
4. Make concrete file changes in the workspace until the project is closer to meeting all requirements.
5. Ensure the implementation stays aligned with the user request and remains server-rendered.
6. In your response, summarize the key files created or changed, the current status, and any remaining risks.

Do not only describe the plan. Apply the changes in the workspace.