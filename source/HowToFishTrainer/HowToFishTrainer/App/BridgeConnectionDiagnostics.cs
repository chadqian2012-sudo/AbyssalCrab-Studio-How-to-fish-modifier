using System;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace HowToFishTrainer.App;

public static class BridgeConnectionDiagnostics
{
	private static readonly object LogGate = new object();

	public static string Describe(Exception exception)
	{
		RuntimeBridgeConnectStage? runtimeBridgeConnectStage = ((exception is RuntimeBridgeConnectException ex) ? new RuntimeBridgeConnectStage?(ex.Stage) : ((RuntimeBridgeConnectStage?)null));
		Exception ex2 = RootCause(exception);
		string text;
		if (!(ex2 is TimeoutException))
		{
			if (!(ex2 is OperationCanceledException))
			{
				if (!(ex2 is UnauthorizedAccessException))
				{
					if (ex2 is Win32Exception ex3)
					{
						if (ex3.NativeErrorCode != 5)
						{
							goto IL_00a1;
						}
						text = "访问被拒绝，请确保游戏与修改器权限一致";
					}
					else if (!(ex2 is EndOfStreamException))
					{
						if (!(ex2 is IOException))
						{
							goto IL_00a1;
						}
						text = "运行时通信中断";
					}
					else
					{
						text = "游戏内运行时已关闭管道";
					}
				}
				else
				{
					text = "访问被拒绝，请确保游戏与修改器权限一致";
				}
			}
			else
			{
				text = "连接超时";
			}
		}
		else
		{
			text = "连接超时";
		}
		goto IL_00c3;
		IL_00a1:
		text = (string.IsNullOrWhiteSpace(ex2.Message) ? ex2.GetType().Name : ex2.Message);
		goto IL_00c3;
		IL_00c3:
		string text2 = text;
		if (runtimeBridgeConnectStage.HasValue)
		{
			return StageText(runtimeBridgeConnectStage.Value) + "：" + text2;
		}
		return text2;
	}

	public static string Append(string directory, int processId, Exception exception, DateTimeOffset? timestamp = null)
	{
		Directory.CreateDirectory(directory);
		string text = Path.Combine(directory, "runtime-bridge.log");
		RuntimeBridgeConnectStage? runtimeBridgeConnectStage = ((exception is RuntimeBridgeConnectException ex) ? new RuntimeBridgeConnectStage?(ex.Stage) : ((RuntimeBridgeConnectStage?)null));
		Exception ex2 = RootCause(exception);
		string contents = $"{timestamp ?? DateTimeOffset.Now:O} PID={processId} Stage={runtimeBridgeConnectStage?.ToString() ?? "Unknown"} {ex2.GetType().Name}: {ex2.Message}{Environment.NewLine}";
		lock (LogGate)
		{
			File.AppendAllText(text, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return text;
		}
	}

	private static Exception RootCause(Exception exception)
	{
		Exception ex = exception;
		while (ex.InnerException != null)
		{
			ex = ex.InnerException;
		}
		return ex;
	}

	private static string StageText(RuntimeBridgeConnectStage stage)
	{
		return stage switch
		{
			RuntimeBridgeConnectStage.CreateTransport => "创建通信通道", 
			RuntimeBridgeConnectStage.DependencyLoad => "加载注入依赖", 
			RuntimeBridgeConnectStage.RuntimeLoad => "加载游戏运行时", 
			RuntimeBridgeConnectStage.PipeHandshake => "等待运行时管道", 
			RuntimeBridgeConnectStage.SessionHandshake => "验证游戏会话", 
			RuntimeBridgeConnectStage.SessionRefresh => "刷新游戏会话", 
			_ => "连接运行时桥", 
		};
	}
}
