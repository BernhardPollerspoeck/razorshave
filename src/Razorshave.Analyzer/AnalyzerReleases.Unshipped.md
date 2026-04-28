; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category              | Severity | Notes
--------|-----------------------|----------|------------------------------------------------------------------
RZS2001 | Razorshave.Transpiler | Error    | Unsupported C# expression kind in Razor component / [Client] class
RZS2002 | Razorshave.Transpiler | Error    | Unsupported C# statement kind in Razor component / [Client] class
RZS2003 | Razorshave.Transpiler | Error    | Unsupported C# pattern kind in is-expressions or switch-expressions
RZS3001 | Razorshave.Transpiler | Error    | User-declared component shadows a runtime component (NavLink/Router/PageTitle)
