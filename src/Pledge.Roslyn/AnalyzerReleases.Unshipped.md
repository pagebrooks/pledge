; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PLEDGE001 | Pledge | Error | Contract call is not at the top of the method
PLEDGE002 | Pledge | Error | Result<T>() used incorrectly
PLEDGE003 | Pledge | Error | Old(...) used outside a postcondition
PLEDGE004 | Pledge | Warning | Impure expression in a contract
PLEDGE005 | Pledge | Warning | Pledge generator is not active
PLEDGE006 | Pledge | Info | Contracted method has no call sites in this compilation
PLEDGE007 | Pledge | Warning | Old(...) of a mutable reference
PLEDGE008 | Pledge | Warning | Constant argument violates a precondition
PLEDGE009 | Pledge | Warning | Contract on an unsupported member
