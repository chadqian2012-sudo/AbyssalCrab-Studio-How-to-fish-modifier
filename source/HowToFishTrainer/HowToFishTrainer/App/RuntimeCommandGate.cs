using System;
using System.Threading;
using System.Threading.Tasks;

namespace HowToFishTrainer.App;

public sealed class RuntimeCommandGate : IDisposable
{
	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
	private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
	private readonly object _lifecycle = new object();
	private int _activeCalls;
	private bool _disposing;
	private bool _shutdownIssued;

	public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, TimeSpan operationTimeout, CancellationToken cancellationToken)
	{
		lock (_lifecycle)
		{
			ObjectDisposedException.ThrowIf(_disposing, this);
			_activeCalls++;
		}
		bool entered = false;
		try
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
			await _gate.WaitAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			entered = true;
			timeout.CancelAfter(operationTimeout);
			return await operation(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			if (entered) _gate.Release();
			lock (_lifecycle)
			{
				_activeCalls--;
				if (_disposing && _shutdownIssued && _activeCalls == 0)
				{
					_gate.Dispose();
					_shutdown.Dispose();
				}
			}
		}
	}

	public void Dispose()
	{
		lock (_lifecycle)
		{
			if (_disposing) return;
			_disposing = true;
		}
		try { _shutdown.Cancel(); }
		finally
		{
			lock (_lifecycle)
			{
				_shutdownIssued = true;
				if (_activeCalls == 0)
				{
					_gate.Dispose();
					_shutdown.Dispose();
				}
			}
		}
	}
}
