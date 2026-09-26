# Subscription System Flow

This flowchart illustrates the complete subscription workflow when a user clicks a `genhub://` protocol link to subscribe to a publisher.

## Overview

The subscription system enables users to discover and subscribe to content publishers through shareable `genhub://` protocol links. Once subscribed, publishers appear in the Downloads UI sidebar, and their catalogs become browsable.

## Flow Diagram

```mermaid
flowchart TD
    Start([User clicks genhub:// link]) --> Parse[Parse protocol URL]
    Parse --> Extract[Extract definition URL from parameters]
    Extract --> Validate{Valid URL?}

    Validate -->|No| ErrorInvalid[Show error: Invalid subscription link]
    ErrorInvalid --> End1([End])

    Validate -->|Yes| CheckExisting{Already subscribed?}
    CheckExisting -->|Yes| ShowExisting[Show info: Already subscribed]
    ShowExisting --> End2([End])

    CheckExisting -->|No| FetchDef[Fetch PublisherDefinition from URL]
    FetchDef --> FetchSuccess{Fetch successful?}

    FetchSuccess -->|No| CheckRetry{Network error?}
    CheckRetry -->|Yes| RetryPrompt[Show retry dialog]
    RetryPrompt --> UserRetry{User retries?}
    UserRetry -->|Yes| FetchDef
    UserRetry -->|No| End3([End])

    CheckRetry -->|No| ErrorFetch[Show error: Invalid definition]
    ErrorFetch --> End4([End])

    FetchSuccess -->|Yes| ValidateDef{Valid definition schema?}
    ValidateDef -->|No| ErrorSchema[Show error: Invalid definition format]
    ErrorSchema --> End5([End])

    ValidateDef -->|Yes| ShowDialog[Show SubscriptionConfirmationViewModel]
    ShowDialog --> DisplayInfo[Display publisher info:<br/>- Name, description<br/>- Avatar, website<br/>- Catalog list<br/>- Referrals]

    DisplayInfo --> UserConfirm{User confirms?}
    UserConfirm -->|No| Cancelled[Subscription cancelled]
    Cancelled --> End6([End])

    UserConfirm -->|Yes| SaveSub[Save to subscriptions.json]
    SaveSub --> UpdateStore[Update PublisherSubscriptionStore]
    UpdateStore --> AddSidebar[Add publisher to Downloads sidebar]

    AddSidebar --> FetchCatalogs[Fetch all catalogs from definition]
    FetchCatalogs --> CatalogLoop{More catalogs?}

    CatalogLoop -->|Yes| FetchCatalog[Fetch catalog JSON]
    FetchCatalog --> CatalogSuccess{Fetch successful?}

    CatalogSuccess -->|No| LogWarning[Log warning: Catalog unavailable]
    LogWarning --> CatalogLoop

    CatalogSuccess -->|Yes| ParseCatalog[Parse PublisherCatalog]
    ParseCatalog --> ValidateCatalog{Valid schema?}

    ValidateCatalog -->|No| LogError[Log error: Invalid catalog]
    LogError --> CatalogLoop

    ValidateCatalog -->|Yes| StoreCatalog[Store catalog in memory]
    StoreCatalog --> CatalogLoop

    CatalogLoop -->|No| UpdateUI[Update Downloads UI]
    UpdateUI --> DisplayContent[Display content in browser]
    DisplayContent --> ShowSuccess[Show success notification]
    ShowSuccess --> End7([End])
```

## Key Components

### Protocol Handler

- **File**: `App.xaml.cs` (protocol registration)
- **Trigger**: `genhub://subscribe?url=<definition-url>`
- **Action**: Activates subscription workflow

### Subscription Confirmation Dialog

- **ViewModel**: `SubscriptionConfirmationViewModel.cs`
- **Purpose**: Display publisher information and request user confirmation
- **Data Displayed**:
  - Publisher name, description, avatar
  - Website and support URLs
  - List of available catalogs
  - Referral publishers (if any)

### Subscription Storage

- **File**: `subscriptions.json` (user data directory)
- **Service**: `PublisherSubscriptionStore.cs`
- **Schema**:

```json
{
  "subscriptions": [
    {
      "publisherId": "unique-id",
      "definitionUrl": "https://...",
      "subscribedDate": "2026-03-15T10:30:00Z",
      "lastUpdated": "2026-03-15T10:30:00Z"
    }
  ]
}
```

### Catalog Fetching

- **Service**: `PublisherDefinitionService.cs`
- **Process**:
  1. Read catalog URLs from definition
  2. Fetch each catalog JSON
  3. Parse and validate schema
  4. Store in memory for UI display

### Downloads UI Integration

- **ViewModel**: `DownloadsBrowserViewModel.cs`
- **Sidebar**: Displays subscribed publishers alongside core providers
- **Content Browser**: Shows catalog content when publisher selected

## Error Handling

### Network Errors

- Retry mechanism with user prompt
- Fallback to mirror URLs (if defined)
- Graceful degradation (show cached data)

### Validation Errors

- Schema version checking
- Required field validation
- URL format validation

### User Experience

- Non-blocking notifications
- Clear error messages
- Undo subscription option

## Related Files

- `GenHub.Core/Models/Providers/PublisherDefinition.cs`
- `GenHub.Core/Services/Publishers/PublisherDefinitionService.cs`
- `GenHub/Features/Content/ViewModels/Catalog/SubscriptionConfirmationViewModel.cs`
- `GenHub/Features/Downloads/ViewModels/DownloadsBrowserViewModel.cs`
- `GenHub.Core/Services/Publishers/PublisherSubscriptionStore.cs`
