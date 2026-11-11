using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HowToFishTrainer.Core;
using HowToFishTrainer.Memory;

namespace HowToFishTrainer.App;

public sealed class RuntimeBridgeClient : IGameRuntimeClient, IAsyncDisposable
{
	private readonly NamedPipeClientStream _pipe;

	private readonly MonoRemoteGateway _gateway;

	private readonly Guid _session = Guid.NewGuid();

	private readonly byte[] _entitlementSecret;

	private readonly RuntimeCommandGate _commands = new RuntimeCommandGate();

	private Exception? _channelFault;
	private int _disposed;
	private static readonly Lazy<byte[]> HarmonyBytes = new Lazy<byte[]>(() => ReadEmbeddedBytes("0Harmony.dll"));
	private static readonly Lazy<byte[]> RuntimeBytes = new Lazy<byte[]>(() => ReadEmbeddedBytes("HowToFishTrainer.Runtime.dll"));

	private static byte[] ReadEmbeddedBytes(string resourceName)
	{
		using Stream resource = typeof(RuntimeBridgeClient).Assembly.GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException("Embedded dependency missing: " + resourceName);
		var bytes = new byte[checked((int)resource.Length)];
		resource.ReadExactly(bytes);
		return bytes;
	}

	public Guid SessionId => _session;

	private RuntimeBridgeClient(NamedPipeClientStream pipe, MonoRemoteGateway gateway, byte[] entitlementSecret)
	{
		_pipe = pipe;
		_gateway = gateway;
		_entitlementSecret = entitlementSecret;
	}

	public static bool IsAccessDenied(Exception exception)
	{
		if (!(exception is UnauthorizedAccessException) && !(exception is Win32Exception { NativeErrorCode: 5 }) && !exception.Message.Contains("Win32=5", StringComparison.OrdinalIgnoreCase))
		{
			if (exception.InnerException != null)
			{
				return IsAccessDenied(exception.InnerException);
			}
			return false;
		}
		return true;
	}

	public static async Task<RuntimeBridgeClient> ConnectAsync(int processId, string gameRoot, CancellationToken cancellationToken, bool sponsor = false)
	{
		cancellationToken.ThrowIfCancellationRequested();
		RuntimeBridgeConnectStage stage = RuntimeBridgeConnectStage.CreateTransport;
		string name = $"HowToFishTrainer.{processId}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
		NamedPipeClientStream pipe = RuntimePipeFactory.CreateClient(name);
		string monoPath = Path.Combine(gameRoot, "MonoBleedingEdge", "EmbedRuntime", "mono-2.0-bdwgc.dll");
		MonoRemoteGateway? gateway = null;
		RuntimeBridgeClient? client = null;
		try
		{
			gateway = new MonoRemoteGateway(processId, monoPath);
			byte[] entitlementSecret = RandomNumberGenerator.GetBytes(32);
			client = new RuntimeBridgeClient(pipe, gateway, entitlementSecret);
			stage = RuntimeBridgeConnectStage.DependencyLoad;
			cancellationToken.ThrowIfCancellationRequested();
			gateway.LoadDependency(HarmonyBytes.Value, "0Harmony.dll");
			cancellationToken.ThrowIfCancellationRequested();
			RuntimeEntitlementTicket entitlement = client.CreateEntitlement(sponsor);
			string configuration = JsonSerializer.Serialize(new
			{
				pipe = name,
				session = client._session.ToString(),
				entitlementSecret = Convert.ToBase64String(entitlementSecret),
				entitlement = entitlement
			});
			stage = RuntimeBridgeConnectStage.RuntimeLoad;
			gateway.LoadRuntimeBridge(RuntimeBytes.Value, configuration);
			stage = RuntimeBridgeConnectStage.PipeHandshake;
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(15.0));
			await pipe.ConnectAsync(timeout.Token);
			return client;
		}
		catch (Exception innerException)
		{
			if (client != null) await client.DisposeAsync();
			else { pipe.Dispose(); gateway?.Dispose(); }
			if (innerException is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
			throw new RuntimeBridgeConnectException(stage, innerException);
		}
	}

	public async Task<RuntimeCommandResult> UpdateEntitlementAsync(bool sponsor, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		RuntimeEntitlementTicket entitlement = CreateEntitlement(sponsor);
		return await ExecuteAsync(new FeatureCommand("session.entitlement.update", JsonSerializer.SerializeToElement(new { entitlement }), Capability.ReadOnly), cancellationToken);
	}

	private RuntimeEntitlementTicket CreateEntitlement(bool sponsor)
	{
		return RuntimeEntitlement.Create(_session, "how-to-fish-trainer", sponsor, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(20.0), _entitlementSecret);
	}

	public async ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (_channelFault != null)
		{
			throw new InvalidOperationException("Runtime channel is faulted; restart the game before reconnecting.", _channelFault);
		}
		try
		{
			return await _commands.RunAsync(async (CancellationToken operationToken) =>
			{
				try
				{
				Guid correlation = Guid.NewGuid();
				byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
				{
					protocolVersion = 1,
					session = _session,
					correlationId = correlation,
					featureId = command.FeatureId,
					arguments = command.Arguments
				});
				byte[] header = new byte[4];
				BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
				await _pipe.WriteAsync(header, operationToken);
				await _pipe.WriteAsync(payload, operationToken);
				await _pipe.FlushAsync(operationToken);
				await _pipe.ReadExactlyAsync(header, operationToken);
				int length = BinaryPrimitives.ReadInt32LittleEndian(header);
				if (length is < 1 or > 262144)
				{
					throw new InvalidDataException("Runtime response size invalid.");
				}
				byte[] response = new byte[length];
				await _pipe.ReadExactlyAsync(response, operationToken);
				using JsonDocument jsonDocument = JsonDocument.Parse(response);
				JsonElement rootElement = jsonDocument.RootElement;
				RuntimeResponseValidator.Validate(rootElement, _session, correlation);
				bool boolean = rootElement.GetProperty("ok").GetBoolean();
				string message = rootElement.GetProperty("message").GetString() ?? string.Empty;
				bool rolledBack = rootElement.TryGetProperty("rollback", out var value) && value.GetBoolean();
				JsonElement? applied = ((rootElement.TryGetProperty("applied", out var value2) && value2.ValueKind != JsonValueKind.Null) ? new JsonElement?(value2.Clone()) : ((rootElement.TryGetProperty("value", out var value3) && value3.ValueKind != JsonValueKind.Null) ? new JsonElement?(value3.Clone()) : ((JsonElement?)null)));
				JsonElement? readBack = ((rootElement.TryGetProperty("readBack", out var value4) && value4.ValueKind != JsonValueKind.Null) ? new JsonElement?(value4.Clone()) : ((JsonElement?)null));
				return new RuntimeCommandResult(boolean, message, applied, readBack, rolledBack);
				}
				catch (Exception fault)
				{
					// Once an operation owns the pipe, cancellation can leave a partial frame or a late reply.
					_channelFault = fault;
					_pipe.Dispose();
					throw;
				}
			}, TimeSpan.FromSeconds(command.FeatureId == "fun.challenge.start" ? 30 : 12), cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception channelFault)
		{
			_channelFault = channelFault;
			_pipe.Dispose();
			throw;
		}
	}

	public async Task<bool> IsHostAsync(CancellationToken cancellationToken)
	{
		return (await InspectSessionAsync(cancellationToken)).Host;
	}

    internal JsonElement? LastInspection { get; private set; }
	public async Task<RuntimeSessionSnapshot> InspectSessionAsync(CancellationToken cancellationToken)
	{
		RuntimeCommandResult runtimeCommandResult = await ExecuteAsync(new FeatureCommand("session.inspect", JsonSerializer.SerializeToElement(new { }), Capability.ReadOnly), cancellationToken);
		if (!runtimeCommandResult.Succeeded || runtimeCommandResult.Applied is not JsonElement session)
			throw new InvalidDataException("Runtime session inspection failed: " + runtimeCommandResult.Message);

		LastInspection = session;
		return new RuntimeSessionSnapshot(
			session.TryGetProperty("host", out var host) && host.GetBoolean(),
			session.TryGetProperty("client", out var client) && client.GetBoolean(),
			session.TryGetProperty("lobby", out var lobby) && lobby.GetBoolean(),
			session.TryGetProperty("lobbyId", out var lobbyId) ? lobbyId.GetString() : null,
			session.TryGetProperty("ownerId", out var owner) && owner.ValueKind == JsonValueKind.Number && owner.TryGetInt32(out int id) ? id : null,
			session.TryGetProperty("sceneHandle", out var scene) && scene.ValueKind == JsonValueKind.Number && scene.TryGetUInt64(out ulong handle) ? handle : 0);
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
		_commands.Dispose();
		try { _pipe.Dispose(); }
		finally { _gateway.Dispose(); }
		return ValueTask.CompletedTask;
	}

}
