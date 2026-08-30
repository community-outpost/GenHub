# Multi-Instance Debugging Support

GenHub supports running multiple instances simultaneously for debugging purposes. This is useful when testing multiple workspaces or debugging concurrent operations.

## Enabling Multi-Instance Mode

### Command Line Argument
Pass `--multi-instance` or `-m` when launching the application:

```bash
GenHub.exe --multi-instance
# or
GenHub.exe -m
```

### Environment Variable
Set the `GENHUB_MULTI_INSTANCE` environment variable to `1`:

```powershell
$env:GENHUB_MULTI_INSTANCE = "1"
GenHub.exe
```

## Behavior

When multi-instance mode is enabled:

- The single-instance lock is bypassed
- Multiple instances can run simultaneously
- Each instance operates independently with its own workspace

## Use Cases

- Testing workspace switching between multiple instances
- Debugging concurrent operations
- Testing profile management across separate sessions

## Limitations

- Ensure different workspaces are used to avoid conflicts
- Some operations may conflict if run simultaneously on the same data
