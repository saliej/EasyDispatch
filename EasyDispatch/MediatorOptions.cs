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
    /// Assemblies to scan for handlers. Required.
    /// </summary>
    public Assembly[] Assemblies { get; set; } = [];

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
    /// Whether to validate that all message types have registered handlers at startup.
    /// Default is false (validate at runtime).
    /// </summary>
    public bool ValidateHandlersAtStartup { get; set; } = false;
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