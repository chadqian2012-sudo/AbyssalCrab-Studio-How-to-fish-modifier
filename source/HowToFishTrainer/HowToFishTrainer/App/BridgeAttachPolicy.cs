using System;

namespace HowToFishTrainer.App;

public sealed class BridgeAttachPolicy
{
	private readonly Func<DateTimeOffset> _clock;

	private int? _processId;

	private DateTimeOffset _nextAttemptAt;

	public int ConsecutiveFailures { get; private set; }

	public string? LastFailure { get; private set; }

	public BridgeAttachPolicy(Func<DateTimeOffset>? clock = null)
	{
		_clock = clock ?? ((Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow));
	}

	public bool TryBegin(int processId)
	{
		EnsureProcess(processId);
		return _clock() >= _nextAttemptAt;
	}

	public void RecordFailure(int processId)
	{
		RecordFailure(processId, null);
	}

	public void RecordFailure(int processId, string? failure)
	{
		EnsureProcess(processId);
		ConsecutiveFailures++;
		if (!string.IsNullOrWhiteSpace(failure))
		{
			LastFailure = failure.Trim();
		}
		int num = Math.Min(ConsecutiveFailures - 1, 4);
		int num2 = Math.Min(15, 1 << num);
		_nextAttemptAt = _clock().AddSeconds(num2);
	}

	public void RecordSuccess(int processId)
	{
		EnsureProcess(processId);
		ConsecutiveFailures = 0;
		LastFailure = null;
		_nextAttemptAt = DateTimeOffset.MinValue;
	}

	public TimeSpan TimeUntilRetry(int processId)
	{
		EnsureProcess(processId);
		TimeSpan timeSpan = _nextAttemptAt - _clock();
		if (!(timeSpan > TimeSpan.Zero))
		{
			return TimeSpan.Zero;
		}
		return timeSpan;
	}

	public string DescribeRetry(int processId)
	{
		int value = Math.Max(1, (int)Math.Ceiling(TimeUntilRetry(processId).TotalSeconds));
		string value2 = (string.IsNullOrWhiteSpace(LastFailure) ? string.Empty : (" · " + LastFailure));
		return $"运行时桥暂时失联{value2} · {value} 秒后自动重连";
	}

	private void EnsureProcess(int processId)
	{
		if (_processId != processId)
		{
			_processId = processId;
			ConsecutiveFailures = 0;
			LastFailure = null;
			_nextAttemptAt = DateTimeOffset.MinValue;
		}
	}

	public void Reset()
	{
		_processId = null;
		ConsecutiveFailures = 0;
		LastFailure = null;
		_nextAttemptAt = DateTimeOffset.MinValue;
	}
}
