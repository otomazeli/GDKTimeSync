# TS-012 consent-gated AI descriptions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-request, selected-task-only AI description consent flow with no AI provider or outbound request.

**Architecture:** Core models the exact allowed request and a provider-neutral generator result. Desktop validates the existing local AI preference, hosts the explicit consent/draft/apply UI state, and registers an unavailable generator that cannot perform network or credential work. Future provider work replaces only the generator implementation.

**Tech Stack:** .NET 10, C# 14, WPF MVVM, `Microsoft.Extensions.DependencyInjection`, xUnit, mocked/fake tests.

## Global Constraints

- Every commit begins with `TS-012`.
- The only allowed AI request fields are the selected Today task’s name, Jira key, and current description.
- No provider, endpoint, model, token, credential, `HttpClient`, network call, or credential-manager access is added in this milestone.
- Opening/cancelling consent does not call the consent service, generator, persistence repository, or any Toggl/Jira/Tempo/Slack client.
- Consent is explicit per request and never persisted. `AiEnabled` is a local preference only; it is not consent.
- A suggestion is a local draft. It must never overwrite a description, create a worklog/time entry, or send Slack until an explicit Apply action edits the selected local item.
- User-visible messages are fixed safe categories; they must not include request text, prompt text, exception text, URLs, responses, credentials, or secrets.
- Automated verification uses mocks/fakes only. Do not launch the desktop app or issue a live request.

---

## File structure and responsibility map

| Area | Files | Responsibility |
| --- | --- | --- |
| Core contract | `DescriptionSuggestionRequest.cs`, `DescriptionSuggestionResult.cs`, `IAssistedTextGenerator.cs` | Immutable allowed payload and provider-neutral result. |
| Desktop policy | `IAiConsentService.cs`, `AiConsentService.cs`, `UnavailableAssistedTextGenerator.cs` | Local preference gate and guaranteed no-provider/no-network outcome. |
| Presentation | `TodayViewModel.cs`, `TodayView.xaml`, `App.xaml.cs` | Selected item, explicit consent panel, draft display, and explicit local apply. |
| Tests | `AiConsentServiceTests.cs`, `TodayViewModelTests.cs` | Scope, consent, unavailable, apply, and no-post boundaries. |

## Task 1: Add provider-neutral selected-task contracts and unavailable policy

**Files:**
- Create: `src/GDK.TimeSync.Core/DescriptionSuggestionRequest.cs`
- Create: `src/GDK.TimeSync.Core/DescriptionSuggestionResult.cs`
- Create: `src/GDK.TimeSync.Core/IAssistedTextGenerator.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/IAiConsentService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/AiConsentService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/UnavailableAssistedTextGenerator.cs`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Create: `tests/GDK.TimeSync.Tests/AiConsentServiceTests.cs`

**Interfaces:**

```csharp
public sealed record DescriptionSuggestionRequest(
    Guid PlannedWorkItemId,
    string TaskName,
    string JiraIssueKey,
    string CurrentDescription);

public sealed record DescriptionSuggestionResult(
    bool IsAvailable,
    string? SuggestedDescription,
    string SafeMessage);

public interface IAssistedTextGenerator
{
    Task<DescriptionSuggestionResult> SuggestAsync(
        DescriptionSuggestionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAiConsentService
{
    bool IsEnabled { get; }
    bool CanSubmit(DescriptionSuggestionRequest request);
}
```

`AiConsentService` reads only `UserSettingsService.Current.AiEnabled`; it does not read credentials or record consent. `UnavailableAssistedTextGenerator` returns `new(false, null, "AI provider is not configured.")` without constructing an HTTP client or accessing any external state. Register both interfaces as singletons in `App.ConfigureServices`.

- [ ] **Step 1: Write failing contract/policy tests.**

```csharp
[Fact]
public async Task Unavailable_generator_returns_fixed_safe_result_without_network_or_secret_echo()
{
    var generator = new UnavailableAssistedTextGenerator();
    var request = new DescriptionSuggestionRequest(Guid.NewGuid(), "Work", "CGM-42", "secret-sentinel");

    var result = await generator.SuggestAsync(request);

    Assert.False(result.IsAvailable);
    Assert.Null(result.SuggestedDescription);
    Assert.Equal("AI provider is not configured.", result.SafeMessage);
    Assert.DoesNotContain("secret-sentinel", result.SafeMessage, StringComparison.Ordinal);
}

[Fact]
public void Consent_policy_requires_enabled_preference_and_nonblank_selected_fields()
{
    var policy = CreatePolicy(aiEnabled: true);

    Assert.True(policy.CanSubmit(new(Guid.NewGuid(), "Work", "CGM-42", "Draft")));
    Assert.False(policy.CanSubmit(new(Guid.NewGuid(), "", "CGM-42", "Draft")));
    Assert.False(CreatePolicy(aiEnabled: false).CanSubmit(new(Guid.NewGuid(), "Work", "CGM-42", "Draft")));
}
```

- [ ] **Step 2: Run focused tests and confirm contracts are absent.**

Run:

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~AiConsentServiceTests
```

Expected: FAIL because the contract and policy types do not yet exist.

- [ ] **Step 3: Implement the minimal immutable contracts and local-only services.**

```csharp
public sealed class UnavailableAssistedTextGenerator : IAssistedTextGenerator
{
    public Task<DescriptionSuggestionResult> SuggestAsync(DescriptionSuggestionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DescriptionSuggestionResult(false, null, "AI provider is not configured."));
}
```

Validate `TaskName`, `JiraIssueKey`, and `CurrentDescription` in `CanSubmit` without retaining a copy. Do not add a package or any provider-specific type.

- [ ] **Step 4: Run focused tests and Release build.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~AiConsentServiceTests
dotnet build GDK.TimeSync.slnx -c Release --no-restore
```

- [ ] **Step 5: Commit Task 1.**

```powershell
git add src/GDK.TimeSync.Core/DescriptionSuggestionRequest.cs src/GDK.TimeSync.Core/DescriptionSuggestionResult.cs src/GDK.TimeSync.Core/IAssistedTextGenerator.cs src/GDK.TimeSync.Desktop/Services/IAiConsentService.cs src/GDK.TimeSync.Desktop/Services/AiConsentService.cs src/GDK.TimeSync.Desktop/Services/UnavailableAssistedTextGenerator.cs src/GDK.TimeSync.Desktop/App.xaml.cs tests/GDK.TimeSync.Tests/AiConsentServiceTests.cs
git commit -m "TS-012 feat: add consent-gated AI contracts"
```

## Task 2: Add explicit Today consent, draft, and local apply state

**Files:**
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/TodayViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/TodayView.xaml`
- Modify: `tests/GDK.TimeSync.Tests/TodayViewModelTests.cs`

**Interfaces:**

`TodayViewModel` consumes the Task 1 `IAiConsentService` and `IAssistedTextGenerator` through optional DI constructor parameters after its existing repository/date parameters. It exposes selected-item and safe state only:

```csharp
public PlannedWorkItemViewModel? SelectedItem { get; }
public DescriptionSuggestionRequest? PendingAiRequest { get; }
public string? SuggestedDescription { get; }
public string? AiStatus { get; }
public bool IsAiConsentVisible { get; }
public RelayCommand OpenAiConsentCommand { get; }
public RelayCommand ConfirmAiConsentCommand { get; }
public RelayCommand CancelAiConsentCommand { get; }
public RelayCommand ApplyAiSuggestionCommand { get; }
```

The `DataGrid.SelectedItem` binds two-way to `SelectedItem`. `OpenAiConsentCommand` snapshots only the selected item’s four local identifiers/text fields into `PendingAiRequest`; it makes no policy/generator/repository/integration call. `CancelAiConsentCommand` clears the pending preview only. `ConfirmAiConsentCommand` calls `CanSubmit` once and, only when allowed, calls `SuggestAsync` once; unavailable outcome maps to its fixed safe message. A returned fake suggestion fills `SuggestedDescription` but leaves `SelectedItem.Description` unchanged. `ApplyAiSuggestionCommand` changes only the still-selected item and clears the draft; existing local persistence behavior then handles that ordinary user edit. It never calls the generator, policy, or integration services.

- [ ] **Step 1: Write failing ViewModel tests for the consent boundary and explicit apply.**

```csharp
[Fact]
public async Task Opening_or_declining_ai_consent_does_not_call_policy_generator_or_post_anything()
{
    var (today, fakes, item) = CreateAiToday();
    today.SelectedItem = item;

    today.OpenAiConsentCommand.Execute(null);
    today.CancelAiConsentCommand.Execute(null);

    Assert.Equal(0, fakes.PolicyCalls + fakes.GeneratorCalls + fakes.IntegrationCalls);
    Assert.Equal("Original description", item.Description);
}

[Fact]
public async Task Confirmed_request_contains_only_selected_item_and_unavailable_result_does_not_edit_it()
{
    var (today, fakes, item) = CreateAiToday(unavailable: true);
    today.SelectedItem = item;
    today.OpenAiConsentCommand.Execute(null);

    await today.ConfirmAiConsentAsync();

    Assert.Equal(new DescriptionSuggestionRequest(item.Id, item.Name, item.JiraIssueKey, "Original description"), fakes.LastRequest);
    Assert.Equal("AI provider is not configured.", today.AiStatus);
    Assert.Equal("Original description", item.Description);
}

[Fact]
public async Task Returned_suggestion_requires_explicit_apply_and_updates_only_selected_item()
{
    var (today, _, item) = CreateAiToday(suggestion: "Suggested description");
    today.SelectedItem = item;
    today.OpenAiConsentCommand.Execute(null);
    await today.ConfirmAiConsentAsync();

    Assert.Equal("Original description", item.Description);
    today.ApplyAiSuggestionCommand.Execute(null);
    Assert.Equal("Suggested description", item.Description);
}
```

- [ ] **Step 2: Run focused tests and confirm the selected-item/consent surface is absent.**

Run:

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~TodayViewModelTests
```

Expected: FAIL with missing AI state/commands or constructor dependencies.

- [ ] **Step 3: Implement the minimal explicit flow and bind the panel.**

```csharp
public void OpenAiConsent()
{
    if (SelectedItem is null || IsAiConsentVisible) return;
    PendingAiRequest = new(SelectedItem.Id, SelectedItem.Name, SelectedItem.JiraIssueKey, SelectedItem.Description);
    IsAiConsentVisible = true;
}

public async Task ConfirmAiConsentAsync(CancellationToken cancellationToken = default)
{
    if (!IsAiConsentVisible || PendingAiRequest is not { } request) return;
    IsAiConsentVisible = false;
    if (!consent.CanSubmit(request)) { AiStatus = "AI assistance is disabled or the selected task is incomplete."; return; }
    var result = await generator.SuggestAsync(request, cancellationToken);
    SuggestedDescription = result.SuggestedDescription;
    AiStatus = result.SafeMessage;
}
```

The inline panel must show only the pending task name, Jira key, and current description; it has **Continue** and **Cancel** buttons. The suggestion area has an **Apply to description** button that is disabled without a draft. Keep the existing reminder that no external posting occurs. Do not create a provider settings section.

- [ ] **Step 4: Run focused TS-012 tests, mocked-safe suite, build, and diff check.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~AiConsentServiceTests|FullyQualifiedName~TodayViewModelTests"
dotnet build GDK.TimeSync.slnx -c Release --no-restore
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName!~WindowsCredentialStoreTests"
git diff --check
```

- [ ] **Step 5: Commit Task 2.**

```powershell
git add src/GDK.TimeSync.Desktop/ViewModels/TodayViewModel.cs src/GDK.TimeSync.Desktop/Views/TodayView.xaml tests/GDK.TimeSync.Tests/TodayViewModelTests.cs
git commit -m "TS-012 feat: add selected-task AI consent flow"
```

## Plan self-review

- Spec coverage: Task 1 establishes the selected-task-only provider-neutral boundary and no-provider result; Task 2 implements explicit consent, draft-only behavior, and explicit local apply.
- Safety coverage: both tasks prohibit network/provider/credential use, persistence of consent, automatic edits/posts, and disclosure of content or secrets.
- Scope: no provider choice, endpoint, credential, model, daily-history context, analytics, or automated description update is included.
- Type consistency: Task 2 uses exactly the Task 1 request/result and service contracts.
- Placeholder scan: no deferred implementation markers or undefined contract references remain.
