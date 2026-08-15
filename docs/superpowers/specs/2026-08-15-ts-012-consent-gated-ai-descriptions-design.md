# TS-012 consent-gated AI descriptions design

## Goal

Establish a safe, provider-neutral workflow for drafting a work description from exactly one selected Today task. TS-012 must never invoke an external AI service or configure a provider.

## Scope and privacy boundary

The request is derived only from the user-selected Today item: task name, Jira key, and current description. It excludes all other daily tasks, history, time-entry data, credentials, Jira payloads, Tempo data, Slack data, and configuration secrets.

The existing `AiEnabled` setting is a local preference. It neither records consent nor enables any automatic request. Consent is per request, is never persisted, and is required even when `AiEnabled` is enabled.

## Architecture

Core exposes a provider-neutral `DescriptionSuggestionRequest` and `IAssistedTextGenerator`. Desktop owns `IAiConsentService` and a default unavailable generator. The unavailable generator returns a fixed provider-not-configured result and performs no network, credential, or factory action. A later provider integration can replace only that generator implementation.

## User flow

1. The user selects a Today task and chooses **Draft AI description**.
2. The app displays an inline consent preview of the three exact request fields.
3. Decline closes the preview without calling a generator or changing the task.
4. Approve invokes the provider-neutral generator for that one request.
5. In TS-012, the fixed unavailable result is shown. No content leaves the app.
6. When a future provider returns a suggestion, it is shown as a draft. It never overwrites the description or posts work. The user must select **Apply to description** to change the selected local task.

## Failure handling

All visible outcomes use fixed safe categories. Errors, URLs, prompts, provider responses, credentials, and task data are not logged, persisted, or included in exception text. Cancelling consent or a request does not retry or make a partial external action.

## Verification

Mock/fake tests prove selected-task-only request shaping, declined consent causes zero generator calls, unavailable provider causes zero network activity, suggestion display does not update a task, explicit apply updates only the selected task, and no integration post is triggered. No desktop launch or live request occurs in automated verification.

## Non-goals

- No AI provider, endpoint, model choice, credential, or network request.
- No automatic description editing or posting.
- No daily-history context, analytics, or persistent consent record.
