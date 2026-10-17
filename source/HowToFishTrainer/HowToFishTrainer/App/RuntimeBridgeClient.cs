using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
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
		using MemoryStream memory = new MemoryStream();
		resource.CopyTo(memory);
		return memory.ToArray();
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
				await ReadExactly(_pipe, header, operationToken);
				int num = BinaryPrimitives.ReadInt32LittleEndian(header);
				if ((num < 1 || num > 262144) ? true : false)
				{
					throw new InvalidDataException("Runtime response size invalid.");
				}
				byte[] response = new byte[num];
				await ReadExactly(_pipe, response, operationToken);
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
			}, TimeSpan.FromSeconds(12.0), cancellationToken);
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
		if (runtimeCommandResult.Succeeded)
		{
			JsonElement? applied = runtimeCommandResult.Applied;
			if (applied.HasValue)
			{
				JsonElement valueOrDefault = applied.GetValueOrDefault();
                LastInspection = valueOrDefault.Clone();
				JsonElement value;
				JsonElement value2;
				JsonElement value3;
				JsonElement value4;
				JsonElement value5;
				int value6 = default;
				JsonElement value7;
				ulong num = default;
				return new RuntimeSessionSnapshot(valueOrDefault.TryGetProperty("host", out value) && value.GetBoolean(), valueOrDefault.TryGetProperty("client", out value2) && value2.GetBoolean(), valueOrDefault.TryGetProperty("lobby", out value3) && value3.GetBoolean(), valueOrDefault.TryGetProperty("lobbyId", out value4) ? value4.GetString() : null, (valueOrDefault.TryGetProperty("ownerId", out value5) && LocalTestSafeTryGetInt32(ref value5, ref value6)) ? new int?(value6) : ((int?)null), (valueOrDefault.TryGetProperty("sceneHandle", out value7) && LocalTestSafeTryGetUInt64(ref value7, ref num)) ? num : 0);
			}
		}
		throw new InvalidDataException("Runtime session inspection failed: " + runtimeCommandResult.Message);
	}

	private static async Task ReadExactly(Stream stream, Memory<byte> target, CancellationToken token)
	{
		int num2;
		for (int offset = 0; offset < target.Length; offset += num2)
		{
			int num = offset;
			num2 = await stream.ReadAsync(target.Slice(num, target.Length - num), token);
			if (num2 == 0)
			{
				throw new EndOfStreamException();
			}
		}
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
		_commands.Dispose();
		try { _pipe.Dispose(); }
		finally { _gateway.Dispose(); }
		return ValueTask.CompletedTask;
	}

	private static bool LocalTestSafeTryGetInt32(ref JsonElement P_0, ref int P_1)
	{
		if (P_0.ValueKind != JsonValueKind.Number)
		{
			P_1 = 0;
			return false;
		}
		return P_0.TryGetInt32(out P_1);
	}

	private static bool LocalTestSafeTryGetUInt64(ref JsonElement P_0, ref ulong P_1)
	{
		if (P_0.ValueKind != JsonValueKind.Number)
		{
			P_1 = 0uL;
			return false;
		}
		return P_0.TryGetUInt64(out P_1);
	}
}
