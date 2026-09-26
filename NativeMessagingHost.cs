using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace GWTP_Windows_POC;

internal sealed class NativeMessagingHost : IDisposable
{
    private readonly MainWindow _pickerWindow;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly CancellationTokenSource _cts = new();
    private string? _pendingRequestId;

    public NativeMessagingHost(MainWindow pickerWindow)
    {
        _pickerWindow = pickerWindow;
        _input = Console.OpenStandardInput();
        _output = Console.OpenStandardOutput();
        _pickerWindow.AuthoringTargetSelected += OnAuthoringTargetSelected;
        _pickerWindow.AuthoringSelectionCancelled += OnAuthoringSelectionCancelled;
    }

    public async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var message = await ReadMessageAsync(_cts.Token);
            if (message is null) break;

            if (!message.RootElement.TryGetProperty("type", out var typeElement))
                continue;

            var type = typeElement.GetString();
            var requestId = message.RootElement.TryGetProperty("requestId", out var requestElement)
                ? requestElement.GetString()
                : null;

            if (type == "ping")
            {
                await WriteMessageAsync(new { type = "pong", requestId });
                continue;
            }

            if (type == "pickTarget")
            {
                if (_pendingRequestId is not null)
                {
                    await WriteMessageAsync(new { type = "error", requestId, code = "selection-in-progress" });
                    continue;
                }

                _pendingRequestId = requestId ?? Guid.NewGuid().ToString("N");
                await _pickerWindow.Dispatcher.InvokeAsync(() => _pickerWindow.BeginAuthoringSelection());
                continue;
            }

            if (type == "cancelPick")
            {
                await _pickerWindow.Dispatcher.InvokeAsync(() => _pickerWindow.CancelAuthoringSelection());
            }
        }
    }

    private async void OnAuthoringTargetSelected(WindowsTargetDescriptor descriptor)
    {
        var requestId = _pendingRequestId;
        _pendingRequestId = null;
        if (requestId is null) return;

        try
        {
            await WriteMessageAsync(new { type = "targetSelected", requestId, target = descriptor });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("NativeMessaging.WriteTargetFailed", ex);
        }
    }

    private async void OnAuthoringSelectionCancelled()
    {
        var requestId = _pendingRequestId;
        _pendingRequestId = null;
        if (requestId is null) return;

        try
        {
            await WriteMessageAsync(new { type = "selectionCancelled", requestId });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("NativeMessaging.WriteCancelFailed", ex);
        }
    }

    private async Task<JsonDocument?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        if (!await ReadExactlyOrEofAsync(_input, lengthBytes, cancellationToken)) return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > 1024 * 1024)
            throw new InvalidDataException("Invalid native messaging message length.");

        var payload = new byte[length];
        if (!await ReadExactlyOrEofAsync(_input, payload, cancellationToken))
            throw new EndOfStreamException();

        return JsonDocument.Parse(payload);
    }

    private async Task WriteMessageAsync(object value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, payload.Length);

        await _output.WriteAsync(lengthBytes);
        await _output.WriteAsync(payload);
        await _output.FlushAsync();
    }

    private static async Task<bool> ReadExactlyOrEofAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) return offset == 0;
            offset += read;
        }
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _pickerWindow.AuthoringTargetSelected -= OnAuthoringTargetSelected;
        _pickerWindow.AuthoringSelectionCancelled -= OnAuthoringSelectionCancelled;
        _cts.Dispose();
    }
}
