using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Editors;

/// <summary>
/// Shared base for the GenHub document editors (WND, Texture, and future INI editors).
/// Boxes in the standard document lifecycle, canvas zoom, clipboard verbs, busy tracking
/// with cancellation, and discard confirmation, so a verb added here propagates to every editor.
/// Editors enable verbs by overriding the matching Can/On members.
/// </summary>
public abstract class EditorToolViewModelBase : ObservableObject, IDisposable
{
    private readonly List<IRelayCommand> _editorCommands = [];
    private readonly List<IAsyncRelayCommand> _asyncCommands = [];
    private readonly INotificationService _notifications;
    private readonly ILocalizationService _localization;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _operationCts;
    private double _zoom = EditorConstants.ZoomDefault;
    private bool _isBusy;
    private bool _isDirty;
    private bool _hasDocument;
    private bool _disposed;

    // Classic constructor by design: the command graph wiring below needs a body,
    // which primary-constructor syntax cannot express (initializers cannot call
    // instance members or capture method groups).

    /// <summary>
    /// Initializes a new instance of the <see cref="EditorToolViewModelBase"/> class.
    /// </summary>
    /// <param name="notifications">The notification service for toasts.</param>
    /// <param name="localization">The localization service.</param>
    /// <param name="dialogs">The dialog service for confirmations.</param>
    protected EditorToolViewModelBase(
        INotificationService notifications,
        ILocalizationService localization,
        IDialogService dialogs)
    {
        _notifications = notifications;
        _localization = localization;
        _dialogs = dialogs;

        NewDocumentCommand = RegisterAsync(new AsyncRelayCommand(OnNewDocumentAsync, () => CanNewDocument));
        OpenFolderCommand = RegisterAsync(new AsyncRelayCommand(OnOpenFolderAsync, () => CanOpenFolder));
        OpenFileCommand = RegisterAsync(new AsyncRelayCommand(OnOpenFileAsync, () => CanOpenFile));
        SaveCommand = RegisterAsync(new AsyncRelayCommand(OnSaveAsync, () => CanSave));
        SaveAsCommand = RegisterAsync(new AsyncRelayCommand(OnSaveAsAsync, () => CanSaveAs));
        UndoCommand = Register(new RelayCommand(OnUndo, () => CanUndo));
        RedoCommand = Register(new RelayCommand(OnRedo, () => CanRedo));
        CopyCommand = Register(new RelayCommand(OnCopy, () => CanCopy));
        CutCommand = Register(new RelayCommand(OnCut, () => CanCut));
        PasteCommand = RegisterAsync(new AsyncRelayCommand(OnPasteAsync, () => CanPaste));
        DuplicateCommand = Register(new RelayCommand(OnDuplicate, () => CanDuplicate));
        DeleteCommand = Register(new RelayCommand(OnDelete, () => CanDelete));
        ZoomInCommand = Register(new RelayCommand(OnZoomIn, () => CanZoom));
        ZoomOutCommand = Register(new RelayCommand(OnZoomOut, () => CanZoom));
        ResetZoomCommand = Register(new RelayCommand(OnResetZoom, () => CanZoom));
        CancelOperationCommand = Register(new RelayCommand(CancelOperation, () => IsBusy));
    }

    /// <summary>
    /// Gets the command creating a new document.
    /// </summary>
    public IAsyncRelayCommand NewDocumentCommand { get; }

    /// <summary>
    /// Gets the command opening a folder in the explorer.
    /// </summary>
    public IAsyncRelayCommand OpenFolderCommand { get; }

    /// <summary>
    /// Gets the command opening a single file.
    /// </summary>
    public IAsyncRelayCommand OpenFileCommand { get; }

    /// <summary>
    /// Gets the command saving the open document.
    /// </summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>
    /// Gets the command saving the open document under a new path.
    /// </summary>
    public IAsyncRelayCommand SaveAsCommand { get; }

    /// <summary>
    /// Gets the command undoing the last edit.
    /// </summary>
    public IRelayCommand UndoCommand { get; }

    /// <summary>
    /// Gets the command redoing the last undone edit.
    /// </summary>
    public IRelayCommand RedoCommand { get; }

    /// <summary>
    /// Gets the command copying the selection.
    /// </summary>
    public IRelayCommand CopyCommand { get; }

    /// <summary>
    /// Gets the command cutting the selection.
    /// </summary>
    public IRelayCommand CutCommand { get; }

    /// <summary>
    /// Gets the command pasting from the clipboard.
    /// </summary>
    public IAsyncRelayCommand PasteCommand { get; }

    /// <summary>
    /// Gets the command duplicating the selection.
    /// </summary>
    public IRelayCommand DuplicateCommand { get; }

    /// <summary>
    /// Gets the command deleting the selection.
    /// </summary>
    public IRelayCommand DeleteCommand { get; }

    /// <summary>
    /// Gets the command zooming the canvas in one step.
    /// </summary>
    public IRelayCommand ZoomInCommand { get; }

    /// <summary>
    /// Gets the command zooming the canvas out one step.
    /// </summary>
    public IRelayCommand ZoomOutCommand { get; }

    /// <summary>
    /// Gets the command resetting the canvas zoom.
    /// </summary>
    public IRelayCommand ResetZoomCommand { get; }

    /// <summary>
    /// Gets the command cancelling the running operation.
    /// </summary>
    public IRelayCommand CancelOperationCommand { get; }

    /// <summary>
    /// Gets or sets the canvas zoom factor.
    /// </summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            double clamped = Math.Clamp(value, ZoomMin, ZoomMax);
            if (SetProperty(ref _zoom, clamped))
            {
                OnPropertyChanged(nameof(ZoomDisplayText));
                OnZoomChanged();
            }
        }
    }

    /// <summary>
    /// Gets the zoom factor display text.
    /// </summary>
    public virtual string ZoomDisplayText => $"{Zoom:P0}";

    /// <summary>
    /// Gets a value indicating whether a cancellable operation is running.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the open document has unsaved changes.
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        protected set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(DocumentTitle));
            }
        }
    }

    /// <summary>
    /// Gets the document title for display.
    /// </summary>
    public virtual string? DocumentTitle => null;

    /// <summary>
    /// Gets or sets a value indicating whether a document is open.
    /// </summary>
    public virtual bool HasDocument
    {
        get => _hasDocument;
        protected set => SetProperty(ref _hasDocument, value);
    }

    /// <summary>
    /// Gets a value indicating whether a new document can be created.
    /// </summary>
    public virtual bool CanNewDocument => true;

    /// <summary>
    /// Gets a value indicating whether a folder can be opened.
    /// </summary>
    public virtual bool CanOpenFolder => true;

    /// <summary>
    /// Gets a value indicating whether a file can be opened.
    /// </summary>
    public virtual bool CanOpenFile => true;

    /// <summary>
    /// Gets a value indicating whether the document can be saved.
    /// </summary>
    public virtual bool CanSave => false;

    /// <summary>
    /// Gets a value indicating whether the document can be saved under a new path.
    /// </summary>
    public virtual bool CanSaveAs => false;

    /// <summary>
    /// Gets a value indicating whether undo is available.
    /// </summary>
    public virtual bool CanUndo => false;

    /// <summary>
    /// Gets a value indicating whether redo is available.
    /// </summary>
    public virtual bool CanRedo => false;

    /// <summary>
    /// Gets a value indicating whether the selection can be copied.
    /// </summary>
    public virtual bool CanCopy => false;

    /// <summary>
    /// Gets a value indicating whether the selection can be cut.
    /// </summary>
    public virtual bool CanCut => false;

    /// <summary>
    /// Gets a value indicating whether the clipboard can be pasted.
    /// </summary>
    public virtual bool CanPaste => false;

    /// <summary>
    /// Gets a value indicating whether the selection can be duplicated.
    /// </summary>
    public virtual bool CanDuplicate => false;

    /// <summary>
    /// Gets a value indicating whether the selection can be deleted.
    /// </summary>
    public virtual bool CanDelete => false;

    /// <summary>
    /// Gets a value indicating whether canvas zoom is available.
    /// </summary>
    public virtual bool CanZoom => true;

    /// <summary>
    /// Gets the notification service for toasts.
    /// </summary>
    protected INotificationService Notifications => _notifications;

    /// <summary>
    /// Gets the localization service.
    /// </summary>
    protected ILocalizationService Localization => _localization;

    /// <summary>
    /// Gets the dialog service for confirmations.
    /// </summary>
    protected IDialogService Dialogs => _dialogs;

    /// <summary>
    /// Refreshes every editor command and document-dependent display property.
    /// </summary>
    public void RefreshEditorCommands()
    {
        if (Avalonia.Application.Current != null && !Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshEditorCommands);
            return;
        }

        foreach (var command in _editorCommands)
        {
            command.NotifyCanExecuteChanged();
        }

        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CanNewDocument));
        OnPropertyChanged(nameof(CanOpenFolder));
        OnPropertyChanged(nameof(CanOpenFile));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanSaveAs));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanCut));
        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(CanDuplicate));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanZoom));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the top level for storage pickers.
    /// </summary>
    /// <returns>The top level, or null outside a desktop lifetime.</returns>
    protected static TopLevel? GetTopLevel()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return null;
        }

        return lifetime.MainWindow is null ? null : TopLevel.GetTopLevel(lifetime.MainWindow);
    }

    /// <summary>
    /// Gets a value indicating whether keyboard focus is inside a text box.
    /// Clipboard and delete verbs must yield so typing and text editing keep working.
    /// </summary>
    /// <returns>True when a text box has focus.</returns>
    protected static bool IsTextInputFocused() =>
        GetTopLevel()?.FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>
    /// Gets the minimum canvas zoom factor.
    /// </summary>
    protected virtual double ZoomMin => EditorConstants.ZoomMin;

    /// <summary>
    /// Gets the maximum canvas zoom factor.
    /// </summary>
    protected virtual double ZoomMax => EditorConstants.ZoomMax;

    /// <summary>
    /// Gets a value indicating whether unsaved changes block destructive actions.
    /// </summary>
    protected virtual bool HasUnsavedChanges => IsDirty;

    /// <summary>
    /// Gets the discard confirmation title resource key.
    /// </summary>
    protected virtual string UnsavedChangesTitleKey => "Common.Editors.UnsavedChanges.Title";

    /// <summary>
    /// Gets the discard confirmation message resource key.
    /// </summary>
    protected virtual string UnsavedChangesMessageKey => "Common.Editors.UnsavedChanges.Message";

    /// <summary>
    /// Gets the discard confirmation confirm-button resource key.
    /// </summary>
    protected virtual string UnsavedChangesDiscardKey => "Common.Editors.UnsavedChanges.Discard";

    /// <summary>
    /// Gets the discard confirmation cancel-button resource key.
    /// </summary>
    protected virtual string UnsavedChangesCancelKey => "Common.Editors.UnsavedChanges.Cancel";

    /// <summary>
    /// Creates a new document.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual Task OnNewDocumentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Opens a folder in the explorer.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual Task OnOpenFolderAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Opens a single file.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual Task OnOpenFileAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Saves the open document.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual Task OnSaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Saves the open document under a new path.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual Task OnSaveAsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Undoes the last edit.
    /// </summary>
    protected virtual void OnUndo()
    {
    }

    /// <summary>
    /// Redoes the last undone edit.
    /// </summary>
    protected virtual void OnRedo()
    {
    }

    /// <summary>
    /// Copies the selection.
    /// </summary>
    protected virtual void OnCopy()
    {
    }

    /// <summary>
    /// Cuts the selection.
    /// </summary>
    protected virtual void OnCut()
    {
    }

    /// <summary>
    /// Pastes from the clipboard.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual Task OnPasteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Duplicates the selection.
    /// </summary>
    protected virtual void OnDuplicate()
    {
    }

    /// <summary>
    /// Deletes the selection.
    /// </summary>
    protected virtual void OnDelete()
    {
    }

    /// <summary>
    /// Zooms the canvas in one step.
    /// </summary>
    protected virtual void OnZoomIn() => Zoom = Math.Min(Zoom + EditorConstants.ZoomStep, ZoomMax);

    /// <summary>
    /// Zooms the canvas out one step.
    /// </summary>
    protected virtual void OnZoomOut() => Zoom = Math.Max(Zoom - EditorConstants.ZoomStep, ZoomMin);

    /// <summary>
    /// Resets the canvas zoom to the default factor.
    /// </summary>
    protected virtual void OnResetZoom() => Zoom = EditorConstants.ZoomDefault;

    /// <summary>
    /// Reacts to zoom factor changes.
    /// </summary>
    protected virtual void OnZoomChanged()
    {
    }

    /// <summary>
    /// Marks the open document as modified.
    /// </summary>
    protected void MarkDirty()
    {
        IsDirty = true;
        RefreshEditorCommands();
    }

    /// <summary>
    /// Marks the open document as saved.
    /// </summary>
    protected void MarkSaved()
    {
        IsDirty = false;
        RefreshEditorCommands();
    }

    /// <summary>
    /// Runs an action as a cancellable busy operation, swallowing cooperative cancellation.
    /// </summary>
    /// <param name="action">The action receiving the operation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected async Task RunOperationAsync(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var owner = BeginOperation();
        try
        {
            await action(owner.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        finally
        {
            EndOperation(owner);
        }
    }

    /// <summary>
    /// Asks the user to confirm discarding unsaved changes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when there is nothing to discard or the user confirmed.</returns>
    protected async Task<bool> ConfirmDiscardUnsavedAsync(CancellationToken cancellationToken)
    {
        if (!HasUnsavedChanges || !HasDocument)
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await Dialogs.ShowConfirmationAsync(
            Localize(UnsavedChangesTitleKey, "Unsaved changes"),
            Localize(UnsavedChangesMessageKey, "Discard unsaved changes?"),
            Localize(UnsavedChangesDiscardKey, "Discard"),
            Localize(UnsavedChangesCancelKey, "Cancel")).ConfigureAwait(true);
    }

    /// <summary>
    /// Localizes a string with an English fallback when the key is missing.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The fallback text.</param>
    /// <param name="args">The optional format arguments.</param>
    /// <returns>The localized or fallback text.</returns>
    protected string Localize(string key, string fallback, params object?[] args)
    {
        if (Localization.TryGetString(key, out string? localized, args))
        {
            return localized;
        }

        return args.Length == 0 ? fallback : string.Format(fallback, args);
    }

    /// <summary>
    /// Releases managed resources.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!disposing)
        {
            return;
        }

        if (_operationCts is not null)
        {
            _operationCts.Cancel();
            _operationCts.Dispose();
            _operationCts = null;
        }
    }

    /// <summary>
    /// Starts a busy operation with a fresh cancellation token.
    /// </summary>
    /// <returns>The source owning the new operation.</returns>
    protected CancellationTokenSource BeginOperation()
    {
        if (_operationCts is not null)
        {
            _operationCts.Cancel();
            _operationCts.Dispose();
        }

        _operationCts = new CancellationTokenSource();
        IsBusy = true;
        RefreshEditorCommands();
        return _operationCts;
    }

    /// <summary>
    /// Ends the running busy operation when the owner still owns it.
    /// A superseded operation ending late must not clear a newer operation's busy state.
    /// </summary>
    /// <param name="owner">The source returned by <see cref="BeginOperation"/>.</param>
    protected void EndOperation(CancellationTokenSource owner)
    {
        if (!ReferenceEquals(_operationCts, owner))
        {
            return;
        }

        IsBusy = false;
        RefreshEditorCommands();
    }

    private TCommand Register<TCommand>(TCommand command)
        where TCommand : IRelayCommand
    {
        _editorCommands.Add(command);
        return command;
    }

    private IAsyncRelayCommand RegisterAsync(IAsyncRelayCommand command)
    {
        _editorCommands.Add(command);
        _asyncCommands.Add(command);
        return command;
    }

    private void CancelOperation()
    {
        _operationCts?.Cancel();
        foreach (var command in _asyncCommands)
        {
            command.Cancel();
        }
    }
}
