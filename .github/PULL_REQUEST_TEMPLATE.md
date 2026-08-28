## Summary

<!-- What does this change do, and why? -->

## Related Issue(s)

<!-- e.g. Closes #123 -->

## Checklist

- [ ] `dotnet test SAM.sln` passes locally, with no failing or skipped tests.
- [ ] `dotnet build SAM.sln -t:Rebuild` completes with 0 warnings and 0 errors.
- [ ] Changes follow the existing architecture: `SAM.Core` stays free of any WPF/UI
      reference, and presentation code stays in `SAM.UI` / `SAM.Picker` / `SAM.Game`.
- [ ] New or changed behavior has test coverage in `SAM.Tests` where practical.
- [ ] Commit history is clean: logically-scoped commits, no leftover "wip"/"fixup" commits,
      no unrelated formatting churn mixed into functional changes.
- [ ] `CHANGELOG.md` updated under `[Unreleased]` for any user-facing change.
