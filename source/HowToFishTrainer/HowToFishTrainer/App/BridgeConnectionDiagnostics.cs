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
		Exception cause = RootCause(exception);
		string message = cause switch
		{
			TimeoutException or OperationCanceledException => "连接超时",
			UnauthorizedAccessException or Win32Exception { NativeErrorCode: 5 } => "访问被拒绝，请确保游戏与修改器权限一致",
			EndOfStreamException => "游戏内运行时已关闭管道",
			IOException => "运行时通信中断",
			_ => string.IsNullOrWhiteSpace(cause.Message) ? cause.GetType().Name : cause.Message
		};
		return exception is RuntimeBridgeConnectException bridge ? StageText(bridge.Stage) + "：" + message : message;
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
