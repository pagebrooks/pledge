# PLEDGE006: Contracted method has no call sites in this compilation

**Severity:** Info.

In interceptor mode, Pledge checks a contract at the places the method is called, not inside the method.
A method that nothing in the same compilation calls therefore has contracts that never run here. Callers
in other assemblies are not intercepted either: interceptors only exist within one compilation.

This is expected for a library's public API, and the diagnostic is informational for that reason. It
matters when you expected a contract to fire and it did not: the method may only be called from another
project, through an interface or base-class reference, by reflection, or from a test project.

Decorator mode (v1.0), which checks inside the member instead of at the call site, will cover these
cases and will be the suggested fix. In `Ignore` mode nothing is checked anyway and the diagnostic is
not reported.
