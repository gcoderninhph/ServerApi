using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using ServerApi.Protos;

namespace ServerApi.Examples;

/// <summary>
/// Example TCP Stream client demonstrating how to connect and communicate
/// with the TCP Stream Gateway
/// </summary>
public class TcpStreamClientExample
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public TcpStreamClientExample(string host = "localhost", int port = 5001)
    {
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(string? jwtToken = null)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(_host, _port);
        _stream = _client.GetStream();

        Console.WriteLine($"Connected to {_host}:{_port}");

        // Optionally send authentication token
        if (!string.IsNullOrEmpty(jwtToken))
        {
            await SendAuthTokenAsync(jwtToken);
            Console.WriteLine("Authentication token sent");
        }
    }

    public async Task SendAuthTokenAsync(string token)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected");

        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var lengthBytes = BitConverter.GetBytes(tokenBytes.Length);

        await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
        await _stream.WriteAsync(tokenBytes, 0, tokenBytes.Length);
        await _stream.FlushAsync();
    }

    public async Task SendMessageAsync(MessageEnvelope envelope)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected");

        var payload = envelope.ToByteArray();
        var lengthBytes = BitConverter.GetBytes(payload.Length);

        Console.WriteLine($"Sending message: Id={envelope.Id}, Type={envelope.Type}, Size={payload.Length} bytes");

        await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
        await _stream.WriteAsync(payload, 0, payload.Length);
        await _stream.FlushAsync();
    }

    public async Task<MessageEnvelope?> ReceiveMessageAsync()
    {
        if (_stream == null) throw new InvalidOperationException("Not connected");

        // Read length prefix
        var lengthBuffer = new byte[4];
        var bytesRead = await ReadExactlyAsync(lengthBuffer, 0, 4);
        if (bytesRead == 0) return null;

        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        Console.WriteLine($"Receiving message of {messageLength} bytes...");

        // Read message payload
        var messageBuffer = new byte[messageLength];
        bytesRead = await ReadExactlyAsync(messageBuffer, 0, messageLength);
        if (bytesRead == 0) return null;

        var envelope = MessageEnvelope.Parser.ParseFrom(messageBuffer);
        Console.WriteLine($"Received message: Id={envelope.Id}, Type={envelope.Type}");

        return envelope;
    }

    private async Task<int> ReadExactlyAsync(byte[] buffer, int offset, int count)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected");

        var totalRead = 0;
        while (totalRead < count)
        {
            var bytesRead = await _stream.ReadAsync(buffer, offset + totalRead, count - totalRead);
            if (bytesRead == 0) return totalRead;
            totalRead += bytesRead;
        }
        return totalRead;
    }

    public void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
        Console.WriteLine("Disconnected");
    }

    // Example usage
    public static async Task Main(string[] args)
    {
        var client = new TcpStreamClientExample();

        try
        {
            // Connect to server
            await client.ConnectAsync();

            // Send a ping message
            var pingEnvelope = new MessageEnvelope
            {
                Id = "ping",
                Type = MessageType.Request,
                Data = ByteString.CopyFromUtf8("{}")
            };

            await client.SendMessageAsync(pingEnvelope);

            // Wait for response
            var response = await client.ReceiveMessageAsync();
            if (response != null)
            {
                Console.WriteLine($"Response: {response.Type}");
                if (response.Type == MessageType.Error)
                {
                    Console.WriteLine($"Error received");
                }
            }

            // Keep connection alive and handle more messages...
            // ...

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            client.Disconnect();
        }
    }
}
