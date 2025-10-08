using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch;

/// <summary>
/// Configuration options for the Mediator.
/// </summary>
public class MediatorOptions
{
	/// <summary>
	/// Assemblies or types to scan for message handlers.
	/// </summary>
	public Assembly[] Assemblies { get; set; } = [];

	/// <summary>
	/// Explicitly registered handler types.
	/// </summary>
	public Type[] HandlerTypes { get; set; } = [];

	/// <summary>
	/// Service lifetime for handlers. Default is Scoped.
	/// </summary>
	public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Scoped;

	/// <summary>
	/// Strategy for publishing notifications when multiple handlers are registered.
	/// Default is StopOnFirstException.
	/// </summary>
	public NotificationPublishStrategy NotificationPublishStrategy { get; set; }
		= NotificationPublishStrategy.StopOnFirstException;

	/// <summary>
	/// Startup validation mode for message handlers.
	/// Default is None (no validation at startup).
	/// </summary>
	public StartupValidation StartupValidation { get; set; } = StartupValidation.None;
}

/// <summary>
/// Defines how notifications are published when multiple handlers exist.
/// </summary>
public enum NotificationPublishStrategy
{
	/// <summary>
	/// Execute handlers sequentially. Stop on first exception.
	/// This is the safest option and maintains ordering guarantees.
	/// </summary>
	StopOnFirstException = 0,

	/// <summary>
	/// Execute handlers sequentially. Continue executing remaining handlers if one throws.
	/// All exceptions are collected and thrown as AggregateException.
	/// </summary>
	ContinueOnException = 1,

	/// <summary>
	/// Execute all handlers in parallel and wait for all to complete.
	/// If any throw, all exceptions are collected as AggregateException.
	/// </summary>
	ParallelWhenAll = 2,

	/// <summary>
	/// Fire and forget - execute handlers in parallel without waiting.
	/// Exceptions are logged but not thrown to caller.
	/// WARNING: No guarantee handlers complete before method returns.
	/// </summary>
	ParallelNoWait = 3
}

/// <summary>
/// Defines startup validation behavior for message handlers.
/// </summary>
public enum StartupValidation
{
	/// <summary>
	/// No validation at startup. Handler resolution errors occur at runtime.
	/// This is the default and fastest startup option.
	/// </summary>
	None = 0,

	/// <summary>
	/// Log warnings for messages without handlers at startup.
	/// Application continues to run but issues are surfaced in logs.
	/// </summary>
	Warn = 1,

	/// <summary>
	/// Throw exception if any messages lack handlers at startup.
	/// Application fails to start if configuration is incomplete.
	/// Recommended for production to catch issues early.
	/// </summary>
	FailFast = 2
}